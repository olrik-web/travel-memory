using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.Azurite;
using TravelMemory.Api.Features.PhotoImports;
using TravelMemory.Api.Features.Trips;
using TravelMemory.Domain.Photos;
using TravelMemory.Persistence.Data;
using TravelMemory.Persistence.Photos;
using TravelMemory.Worker;

namespace TravelMemory.IntegrationTests.Integration;

[Collection(ContainerTestCollection.Name)]
public sealed class PhotoImportFlowTests(SqlServerFixture sqlServer) : IAsyncLifetime
{
    private static readonly Guid OwnerId =
        Guid.Parse("78cb7c99-b0f4-42a8-acbb-45b800cd9fb8");

    private readonly string databaseConnectionString =
        sqlServer.CreateDatabaseConnectionString();
    private readonly AzuriteContainer azurite =
        new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.35.0")
            .WithCommand("--skipApiVersionCheck")
            .Build();

    public async ValueTask InitializeAsync() => await azurite.StartAsync();

    public ValueTask DisposeAsync() => azurite.DisposeAsync();

    [Fact]
    public async Task Imports_jpeg_and_heic_with_offset_deduplication_and_safe_cleanup()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var trip = await CreateTripAsync(client);
        var jpeg = CreateOrientedJpeg();
        var heic = await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.heic"));
        var batchRequest = new CreatePhotoImportRequest(
            Guid.NewGuid(),
            [
                CreateFile("oriented.jpg", "image/jpeg", jpeg),
                CreateFile("sample.heic", "image/heic", heic),
            ]);

        var createResponse = await client.PostAsJsonAsync(
            $"/api/trips/{trip.Id}/photo-imports",
            batchRequest);
        Assert.True(
            createResponse.StatusCode == HttpStatusCode.Created,
            $"Expected Created but received {createResponse.StatusCode}: {await createResponse.Content.ReadAsStringAsync()}");
        var batch = Assert.IsType<PhotoImportBatchResponse>(
            await createResponse.Content.ReadFromJsonAsync<PhotoImportBatchResponse>());
        Assert.Equal(2, batch.Items.Count);
        Assert.All(batch.Items, AssertLeastPrivilegeUploadGrant);

        var idempotentResponse = await client.PostAsJsonAsync(
            $"/api/trips/{trip.Id}/photo-imports",
            batchRequest);
        Assert.Equal(HttpStatusCode.OK, idempotentResponse.StatusCode);
        var idempotentBatch = Assert.IsType<PhotoImportBatchResponse>(
            await idempotentResponse.Content.ReadFromJsonAsync<PhotoImportBatchResponse>());
        Assert.Equal(batch.Id, idempotentBatch.Id);

        await UploadAndCompleteAsync(
            client,
            batch.Id,
            Assert.Single(batch.Items, item => item.FileName == "oriented.jpg"),
            jpeg);
        var heicItem = Assert.Single(batch.Items, item => item.FileName == "sample.heic");
        await UploadAndCompleteAsync(client, batch.Id, heicItem, heic);

        var repeatedCompletion = await client.PostAsync(
            $"/api/photo-imports/{batch.Id}/items/{heicItem.Id}/complete-upload",
            content: null);
        Assert.Equal(HttpStatusCode.Accepted, repeatedCompletion.StatusCode);

        await ProcessAllPendingJobsAsync();
        batch = await GetBatchAsync(client, batch.Id);
        Assert.Equal(nameof(PhotoImportBatchState.ReadyForReview), batch.State);
        Assert.Equal(2, batch.Counts.Ready);

        var preview = Assert.IsType<PhotoTimePreviewResponse>(
            await client.GetFromJsonAsync<PhotoTimePreviewResponse>(
                $"/api/photo-imports/{batch.Id}/time-preview?adjustmentMinutes=-120"));
        Assert.Equal(2, preview.Items.Count);
        Assert.All(
            preview.Items,
            item => Assert.Equal(
                item.CapturedAtOriginalLocal.AddMinutes(-120),
                item.CapturedAtTimelineLocal));

        var finalizeResponse = await client.PostAsJsonAsync(
            $"/api/photo-imports/{batch.Id}/finalize",
            new FinalizePhotoImportRequest(-120));
        Assert.Equal(HttpStatusCode.Accepted, finalizeResponse.StatusCode);
        await ProcessAllPendingJobsAsync();

        batch = await GetBatchAsync(client, batch.Id);
        Assert.Equal(nameof(PhotoImportBatchState.Completed), batch.State);
        Assert.Equal(2, batch.Counts.Succeeded);
        Assert.All(batch.Items, item => Assert.NotNull(item.CapturedAtOriginalLocal));

        var timeline = Assert.IsType<PhotoTimelineResponse>(
            await client.GetFromJsonAsync<PhotoTimelineResponse>(
                $"/api/trips/{trip.Id}/photos"));
        Assert.Equal(2, timeline.Items.Count);
        Assert.All(timeline.Items, item => Assert.Equal(-120, item.TimeAdjustmentMinutes));
        var orientedPhoto = Assert.Single(
            timeline.Items,
            item => item.FileName == "oriented.jpg");
        Assert.Equal(20, orientedPhoto.Width);
        Assert.Equal(40, orientedPhoto.Height);
        Assert.All(timeline.Items, item => Assert.NotEqual(item.ThumbnailUrl, item.WebUrl));

        var blobService = new BlobServiceClient(azurite.GetConnectionString());
        Assert.Equal(
            4,
            await CountBlobsAsync(
                blobService.GetBlobContainerClient(PhotoStorageNames.PermanentContainer)));
        Assert.Equal(
            0,
            await CountBlobsAsync(
                blobService.GetBlobContainerClient(PhotoStorageNames.TemporaryContainer)));

        var duplicateBatch = await CreateSingleFileBatchAsync(
            client,
            trip.Id,
            "duplicate.jpg",
            "image/jpeg",
            jpeg);
        await UploadAndCompleteAsync(
            client,
            duplicateBatch.Id,
            Assert.Single(duplicateBatch.Items),
            jpeg);
        await ProcessAllPendingJobsAsync();
        duplicateBatch = await GetBatchAsync(client, duplicateBatch.Id);

        Assert.Equal(nameof(PhotoImportBatchState.Completed), duplicateBatch.State);
        Assert.Equal(1, duplicateBatch.Counts.Duplicates);
        var timelineAfterDuplicate = Assert.IsType<PhotoTimelineResponse>(
            await client.GetFromJsonAsync<PhotoTimelineResponse>(
                $"/api/trips/{trip.Id}/photos"));
        Assert.Equal(2, timelineAfterDuplicate.Items.Count);

        var invalidBytes = "not a jpeg"u8.ToArray();
        var failedBatch = await CreateSingleFileBatchAsync(
            client,
            trip.Id,
            "broken.jpg",
            "image/jpeg",
            invalidBytes);
        await UploadAndCompleteAsync(
            client,
            failedBatch.Id,
            Assert.Single(failedBatch.Items),
            invalidBytes);
        await ProcessAllPendingJobsAsync();
        failedBatch = await GetBatchAsync(client, failedBatch.Id);

        Assert.Equal(nameof(PhotoImportBatchState.CompletedWithErrors), failedBatch.State);
        var failedItem = Assert.Single(failedBatch.Items);
        Assert.Equal("image_decode_failed", failedItem.ErrorCode);
        Assert.Contains("damaged", failedItem.ErrorMessage);
        Assert.NotNull(failedItem.OriginalRetainedUntilUtc);
        Assert.InRange(
            failedItem.OriginalRetainedUntilUtc.Value - DateTimeOffset.UtcNow,
            TimeSpan.FromDays(6.9),
            TimeSpan.FromDays(7.1));
        Assert.Equal(
            1,
            await CountBlobsAsync(
                blobService.GetBlobContainerClient(PhotoStorageNames.TemporaryContainer)));

        await using var verificationContext = CreateDbContext();
        Assert.Equal(
            1,
            await verificationContext.PhotoProcessingJobs.CountAsync(
                job =>
                    job.ImportItemId == heicItem.Id
                    && job.Kind == PhotoProcessingJobKind.Analyze));
        Assert.True(
            await verificationContext.PhotoProcessingJobs.AnyAsync(
                job =>
                    job.ImportItemId == failedItem.Id
                    && job.Kind == PhotoProcessingJobKind.ExpireFailedOriginal
                    && job.State == PhotoProcessingJobState.Pending));
        Assert.All(
            await verificationContext.PhotoImportItems
                .Where(item => item.ImportBatchId == batch.Id)
                .ToListAsync(),
            item => Assert.NotNull(item.OriginalDeletedAtUtc));
    }

    [Fact]
    public async Task Accepts_500_files_and_rejects_501()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var trip = await CreateTripAsync(client);
        var files = Enumerable.Range(0, PhotoImportBatch.MaximumFileCount)
            .Select(index => new CreatePhotoImportFileRequest(
                Convert.ToHexStringLower(SHA256.HashData(BitConverter.GetBytes(index))),
                $"photo-{index:D3}.jpg",
                "image/jpeg",
                1))
            .ToArray();

        var accepted = await client.PostAsJsonAsync(
            $"/api/trips/{trip.Id}/photo-imports",
            new CreatePhotoImportRequest(Guid.NewGuid(), files));
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        var batch = Assert.IsType<PhotoImportBatchResponse>(
            await accepted.Content.ReadFromJsonAsync<PhotoImportBatchResponse>());
        Assert.Equal(PhotoImportBatch.MaximumFileCount, batch.Items.Count);

        var rejected = await client.PostAsJsonAsync(
            $"/api/trips/{trip.Id}/photo-imports",
            new CreatePhotoImportRequest(
                Guid.NewGuid(),
                [
                    .. files,
                    new CreatePhotoImportFileRequest(
                        new string('a', 64),
                        "one-too-many.jpg",
                        "image/jpeg",
                        1),
                ]));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Fact]
    public async Task Worker_fails_a_job_with_an_unexpected_error_and_keeps_processing()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var trip = await CreateTripAsync(client);
        var jpeg = CreateOrientedJpeg();
        var missingBatch = await CreateSingleFileBatchAsync(
            client,
            trip.Id,
            "missing.jpg",
            "image/jpeg",
            jpeg);
        var missingItem = Assert.Single(missingBatch.Items);
        await UploadAndCompleteAsync(client, missingBatch.Id, missingItem, jpeg);

        // Deleting the uploaded original before the worker starts makes the analysis
        // download fail with a 404, which is neither a photo processing error nor a
        // transient storage error.
        var blobService = new BlobServiceClient(azurite.GetConnectionString());
        var temporaryContainer =
            blobService.GetBlobContainerClient(PhotoStorageNames.TemporaryContainer);
        await foreach (var blob in temporaryContainer.GetBlobsAsync())
        {
            await temporaryContainer.DeleteBlobAsync(blob.Name);
        }

        await using var workerServices = CreateWorkerServices();
        var worker = workerServices.GetRequiredService<PhotoQueueWorker>();
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var failedItem = await WaitForSingleItemAsync(
                client,
                missingBatch.Id,
                item => item.State == nameof(PhotoImportItemState.Failed));
            Assert.Equal("unexpected_error", failedItem.ErrorCode);
            Assert.True(failedItem.CanRetry);
            Assert.NotNull(failedItem.OriginalRetainedUntilUtc);

            var heic = await File.ReadAllBytesAsync(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.heic"));
            var laterBatch = await CreateSingleFileBatchAsync(
                client,
                trip.Id,
                "sample.heic",
                "image/heic",
                heic);
            await UploadAndCompleteAsync(
                client,
                laterBatch.Id,
                Assert.Single(laterBatch.Items),
                heic);
            await WaitForSingleItemAsync(
                client,
                laterBatch.Id,
                item => item.State == nameof(PhotoImportItemState.ReadyForReview));

            Assert.False(worker.ExecuteTask?.IsCompleted);
            await using var verificationContext = CreateDbContext();
            var failedJob = await verificationContext.PhotoProcessingJobs.SingleAsync(
                job =>
                    job.ImportItemId == missingItem.Id
                    && job.Kind == PhotoProcessingJobKind.Analyze);
            Assert.Equal(PhotoProcessingJobState.Failed, failedJob.State);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Rejects_retry_after_the_failed_original_was_deleted()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var trip = await CreateTripAsync(client);
        var jpeg = CreateOrientedJpeg();
        var batch = await CreateSingleFileBatchAsync(
            client,
            trip.Id,
            "expired.jpg",
            "image/jpeg",
            jpeg);
        var item = Assert.Single(batch.Items);
        await UploadAndCompleteAsync(client, batch.Id, item, jpeg);

        // Simulate an exhausted item whose 7-day retention has already expired.
        await using (var context = CreateDbContext())
        {
            var storedItem = await context.PhotoImportItems.SingleAsync(
                value => value.Id == item.Id);
            var now = DateTimeOffset.UtcNow;
            var storedJob = await context.PhotoProcessingJobs.SingleAsync(
                value => value.ImportItemId == item.Id);
            Assert.True(storedJob.TryStart(now));
            storedJob.Fail("Processing failed.", now);
            storedItem.MarkFailed("processing_exhausted", "Processing failed.", now);
            storedItem.MarkFailedOriginalDeleted(now);
            await context.SaveChangesAsync();
        }

        var response = await client.PostAsync(
            $"/api/photo-imports/{batch.Id}/items/{item.Id}/retry",
            content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False(Assert.Single((await GetBatchAsync(client, batch.Id)).Items).CanRetry);
    }

    [Fact]
    public async Task Keeps_retrying_failed_original_expiration_without_touching_the_item_error()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var trip = await CreateTripAsync(client);
        var invalidBytes = "not a jpeg"u8.ToArray();
        var batch = await CreateSingleFileBatchAsync(
            client,
            trip.Id,
            "broken.jpg",
            "image/jpeg",
            invalidBytes);
        var item = Assert.Single(batch.Items);
        await UploadAndCompleteAsync(client, batch.Id, item, invalidBytes);
        await ProcessAllPendingJobsAsync();

        Guid expirationJobId;
        string temporaryBlobName;
        await using (var context = CreateDbContext())
        {
            expirationJobId = (await context.PhotoProcessingJobs.SingleAsync(
                job =>
                    job.ImportItemId == item.Id
                    && job.Kind == PhotoProcessingJobKind.ExpireFailedOriginal)).Id;
            temporaryBlobName = (await context.PhotoImportItems.SingleAsync(
                value => value.Id == item.Id)).TemporaryBlobName;
        }

        // A lease makes the delete fail with a non-transient 412, an unexpected error.
        var temporaryBlob = new BlobServiceClient(azurite.GetConnectionString())
            .GetBlobContainerClient(PhotoStorageNames.TemporaryContainer)
            .GetBlobClient(temporaryBlobName);
        var lease = temporaryBlob.GetBlobLeaseClient();
        await lease.AcquireAsync(TimeSpan.FromSeconds(60));
        var afterRetention = DateTimeOffset.UtcNow.AddDays(8);
        await ProcessJobAsync(expirationJobId, new ManualTimeProvider(afterRetention));

        await using (var context = CreateDbContext())
        {
            var job = await context.PhotoProcessingJobs.SingleAsync(
                value => value.Id == expirationJobId);
            Assert.Equal(PhotoProcessingJobState.Pending, job.State);
            var storedItem = await context.PhotoImportItems.SingleAsync(
                value => value.Id == item.Id);
            Assert.Equal("image_decode_failed", storedItem.ErrorCode);
            Assert.Null(storedItem.OriginalDeletedAtUtc);
        }

        await lease.ReleaseAsync();
        await ProcessJobAsync(expirationJobId, new ManualTimeProvider(afterRetention.AddHours(2)));

        Assert.False(await temporaryBlob.ExistsAsync());
        var expiredItem = Assert.Single((await GetBatchAsync(client, batch.Id)).Items);
        Assert.Equal("image_decode_failed", expiredItem.ErrorCode);
        Assert.NotNull(expiredItem.OriginalDeletedAtUtc);
    }

    [Fact]
    public async Task Redispatches_only_jobs_without_a_recent_message()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var trip = await CreateTripAsync(client);
        var jpeg = CreateOrientedJpeg();
        var batch = await CreateSingleFileBatchAsync(
            client,
            trip.Id,
            "queued.jpg",
            "image/jpeg",
            jpeg);
        await UploadAndCompleteAsync(client, batch.Id, Assert.Single(batch.Items), jpeg);
        var queue = new QueueServiceClient(azurite.GetConnectionString())
            .GetQueueClient(PhotoStorageNames.ProcessingQueue);
        Assert.Equal(1, await CountQueueMessagesAsync(queue));

        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow.AddMinutes(1));
        await using var workerServices = CreateWorkerServices(clock);
        var maintenance = workerServices.GetRequiredService<PhotoMaintenance>();
        await maintenance.RunAsync(CancellationToken.None);
        Assert.Equal(1, await CountQueueMessagesAsync(queue));

        // Once the message is lost, the job is dispatched again after the timeout, and the
        // new message again suppresses redispatch in the following cycle.
        await queue.ClearMessagesAsync();
        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        await maintenance.RunAsync(CancellationToken.None);
        Assert.Equal(1, await CountQueueMessagesAsync(queue));

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        await maintenance.RunAsync(CancellationToken.None);
        Assert.Equal(1, await CountQueueMessagesAsync(queue));
    }

    [Fact]
    public async Task Imports_one_of_two_identical_photos_processed_concurrently()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var trip = await CreateTripAsync(client);
        var jpeg = CreateOrientedJpeg();
        var batches = new List<PhotoImportBatchResponse>();
        foreach (var fileName in new[] { "first.jpg", "second.jpg" })
        {
            var batch = await CreateSingleFileBatchAsync(
                client,
                trip.Id,
                fileName,
                "image/jpeg",
                jpeg);
            await UploadAndCompleteAsync(client, batch.Id, Assert.Single(batch.Items), jpeg);
            batches.Add(batch);
        }

        await ProcessAllPendingJobsAsync();
        foreach (var batch in batches)
        {
            var finalizeResponse = await client.PostAsJsonAsync(
                $"/api/photo-imports/{batch.Id}/finalize",
                new FinalizePhotoImportRequest(0));
            Assert.Equal(HttpStatusCode.Accepted, finalizeResponse.StatusCode);
        }

        var firstJobId = await GetProcessJobIdAsync(Assert.Single(batches[0].Items).Id);
        var secondJobId = await GetProcessJobIdAsync(Assert.Single(batches[1].Items).Id);

        // The first photo is imported just before the second one is saved, after the second
        // one has passed its duplicate check: the race two worker processes can produce.
        await using (var context = CreateDbContext(
            new BeforePhotoInsertInterceptor(
                () => ProcessJobAsync(firstJobId, TimeProvider.System))))
        {
            var processor = new PhotoJobProcessor(
                context,
                new BlobServiceClient(azurite.GetConnectionString()),
                new PhotoImageProcessor(),
                TimeProvider.System,
                NullLogger<PhotoJobProcessor>.Instance);
            await processor.ProcessAsync(secondJobId, CancellationToken.None);
        }

        await ProcessAllPendingJobsAsync();

        var first = Assert.Single((await GetBatchAsync(client, batches[0].Id)).Items);
        Assert.Equal(nameof(PhotoImportItemState.Succeeded), first.State);
        Assert.Equal(nameof(PhotoImportItemOutcome.Imported), first.Outcome);
        var second = Assert.Single((await GetBatchAsync(client, batches[1].Id)).Items);
        Assert.Equal(nameof(PhotoImportItemState.Succeeded), second.State);
        Assert.Equal(nameof(PhotoImportItemOutcome.Duplicate), second.Outcome);

        var blobService = new BlobServiceClient(azurite.GetConnectionString());
        Assert.Equal(
            2,
            await CountBlobsAsync(
                blobService.GetBlobContainerClient(PhotoStorageNames.PermanentContainer)));
        Assert.Equal(
            0,
            await CountBlobsAsync(
                blobService.GetBlobContainerClient(PhotoStorageNames.TemporaryContainer)));
    }

    [Fact]
    public async Task Concurrent_upload_completions_are_accepted_with_one_analyze_job()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var trip = await CreateTripAsync(client);
        var jpeg = CreateOrientedJpeg();
        var batch = await CreateSingleFileBatchAsync(
            client,
            trip.Id,
            "double-click.jpg",
            "image/jpeg",
            jpeg);
        var item = Assert.Single(batch.Items);
        await UploadAsync(item, jpeg);

        // Several calls make it very likely that at least two pass the AwaitingUpload
        // check before either saves, like a client retry racing the original request.
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => client.PostAsync(
                $"/api/photo-imports/{batch.Id}/items/{item.Id}/complete-upload",
                content: null)));

        Assert.All(
            responses,
            response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        await using var context = CreateDbContext();
        Assert.Equal(
            1,
            await context.PhotoProcessingJobs.CountAsync(
                job =>
                    job.ImportItemId == item.Id
                    && job.Kind == PhotoProcessingJobKind.Analyze));
    }

    [Fact]
    public async Task Fails_abandoned_jobs_that_have_used_up_their_attempts()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var trip = await CreateTripAsync(client);
        var jpeg = CreateOrientedJpeg();
        var batches = new List<PhotoImportBatchResponse>();
        foreach (var fileName in new[] { "crashes-every-time.jpg", "crashed-once.jpg" })
        {
            var batch = await CreateSingleFileBatchAsync(
                client,
                trip.Id,
                fileName,
                "image/jpeg",
                jpeg);
            await UploadAndCompleteAsync(client, batch.Id, Assert.Single(batch.Items), jpeg);
            batches.Add(batch);
        }

        var startedAt = DateTimeOffset.UtcNow;
        var exhaustedItemId = Assert.Single(batches[0].Items).Id;
        var recoverableItemId = Assert.Single(batches[1].Items).Id;
        await SimulateCrashedAttemptsAsync(
            exhaustedItemId,
            PhotoProcessingJob.MaximumAttempts,
            startedAt);
        await SimulateCrashedAttemptsAsync(recoverableItemId, 1, startedAt);

        var clock = new ManualTimeProvider(startedAt.AddHours(2));
        await using var workerServices = CreateWorkerServices(clock);
        await workerServices.GetRequiredService<PhotoMaintenance>()
            .RunAsync(CancellationToken.None);

        var exhaustedItem = Assert.Single((await GetBatchAsync(client, batches[0].Id)).Items);
        Assert.Equal(nameof(PhotoImportItemState.Failed), exhaustedItem.State);
        Assert.Equal("processing_exhausted", exhaustedItem.ErrorCode);
        Assert.True(exhaustedItem.CanRetry);
        Assert.NotNull(exhaustedItem.OriginalRetainedUntilUtc);
        await using var context = CreateDbContext();
        var exhaustedJobs = await context.PhotoProcessingJobs
            .Where(job => job.ImportItemId == exhaustedItemId)
            .ToListAsync();
        Assert.Equal(
            PhotoProcessingJobState.Failed,
            Assert.Single(exhaustedJobs, job => job.Kind == PhotoProcessingJobKind.Analyze)
                .State);
        Assert.Contains(
            exhaustedJobs,
            job => job.Kind == PhotoProcessingJobKind.ExpireFailedOriginal);
        var recoveredJob = await context.PhotoProcessingJobs.SingleAsync(
            job => job.ImportItemId == recoverableItemId);
        Assert.Equal(PhotoProcessingJobState.Pending, recoveredJob.State);
    }

    [Fact]
    public async Task Dispatches_a_job_in_the_next_cycle_after_a_failed_enqueue()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var trip = await CreateTripAsync(client);
        var jpeg = CreateOrientedJpeg();
        var batch = await CreateSingleFileBatchAsync(
            client,
            trip.Id,
            "queue-outage.jpg",
            "image/jpeg",
            jpeg);
        var item = Assert.Single(batch.Items);
        await UploadAsync(item, jpeg);

        // Deleting the queue makes the API's enqueue fail like a queue outage.
        var queue = new QueueServiceClient(azurite.GetConnectionString())
            .GetQueueClient(PhotoStorageNames.ProcessingQueue);
        await queue.DeleteAsync();
        var completion = await client.PostAsync(
            $"/api/photo-imports/{batch.Id}/items/{item.Id}/complete-upload",
            content: null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, completion.StatusCode);
        Assert.Contains("automatically", await completion.Content.ReadAsStringAsync());

        await queue.CreateAsync();
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow.AddMinutes(1));
        await using var workerServices = CreateWorkerServices(clock);
        await workerServices.GetRequiredService<PhotoMaintenance>()
            .RunAsync(CancellationToken.None);

        Assert.Equal(1, await CountQueueMessagesAsync(queue));
    }

    [Fact]
    public async Task Continues_the_request_trace_through_the_queue_into_the_worker()
    {
        var traceId = ActivityTraceId.CreateRandom();
        var spans = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name is "Microsoft.AspNetCore" or "TravelMemory.Worker",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == traceId)
                {
                    spans.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var trip = await CreateTripAsync(client);
        var jpeg = CreateOrientedJpeg();
        var batch = await CreateSingleFileBatchAsync(
            client,
            trip.Id,
            "traced.jpg",
            "image/jpeg",
            jpeg);
        var item = Assert.Single(batch.Items);
        await UploadAsync(item, jpeg);
        using var completion = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/photo-imports/{batch.Id}/items/{item.Id}/complete-upload");
        completion.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(completion)).StatusCode);

        await using (var context = CreateDbContext())
        {
            var job = await context.PhotoProcessingJobs.SingleAsync(
                value => value.ImportItemId == item.Id);
            Assert.Contains(traceId.ToString(), job.TraceParent);
        }

        var queue = new QueueServiceClient(azurite.GetConnectionString())
            .GetQueueClient(PhotoStorageNames.ProcessingQueue);
        var message = Assert.Single((await queue.PeekMessagesAsync()).Value);
        Assert.Contains(traceId.ToString(), message.MessageText);

        await using var workerServices = CreateWorkerServices();
        var worker = workerServices.GetRequiredService<PhotoQueueWorker>();
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitForSingleItemAsync(
                client,
                batch.Id,
                value => value.State == nameof(PhotoImportItemState.ReadyForReview));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        var received = Assert.Single(spans, span => span.OperationName == "receive photo job");
        Assert.Equal(ActivityKind.Consumer, received.Kind);
        var analyzed = Assert.Single(spans, span => span.OperationName == "Analyze photo");
        Assert.Equal(received.SpanId, analyzed.ParentSpanId);
    }

    [Fact]
    public async Task Maintenance_mode_runs_one_cycle_and_exits()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var trip = await CreateTripAsync(client);
        var jpeg = CreateOrientedJpeg();
        var batch = await CreateSingleFileBatchAsync(
            client,
            trip.Id,
            "waiting.jpg",
            "image/jpeg",
            jpeg);
        var item = Assert.Single(batch.Items);
        await UploadAndCompleteAsync(client, batch.Id, item, jpeg);

        // Simulate an enqueue that failed while the worker was scaled to zero: the job is
        // pending with no message, so only the maintenance run can dispatch it.
        var queue = new QueueServiceClient(azurite.GetConnectionString())
            .GetQueueClient(PhotoStorageNames.ProcessingQueue);
        await queue.ClearMessagesAsync();
        await using (var context = CreateDbContext())
        {
            var job = await context.PhotoProcessingJobs.SingleAsync(
                value => value.ImportItemId == item.Id);
            job.MarkDispatchFailed();
            await context.SaveChangesAsync();
        }

        var exitCode = await WorkerProgram.Main(
            [
                WorkerProgram.MaintenanceArgument,
                $"--ConnectionStrings:travelmemory={databaseConnectionString}",
                $"--ConnectionStrings:blobs={azurite.GetConnectionString()}",
                $"--ConnectionStrings:queues={azurite.GetConnectionString()}",
            ])
            .WaitAsync(TimeSpan.FromMinutes(1));

        Assert.Equal(0, exitCode);
        Assert.Equal(1, await CountQueueMessagesAsync(queue));
    }

    [Fact]
    public async Task Deletes_a_photo_with_its_derivatives_and_allows_importing_it_again()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var trip = await CreateTripAsync(client);
        var jpeg = CreateOrientedJpeg();
        await ImportAndFinalizeAsync(client, trip.Id, "delete-me.jpg", jpeg);
        var photo = Assert.Single((await GetTimelineAsync(client, trip.Id)).Items);
        var permanent = new BlobServiceClient(azurite.GetConnectionString())
            .GetBlobContainerClient(PhotoStorageNames.PermanentContainer);
        Assert.Equal(2, await CountBlobsAsync(permanent));

        using (var otherFactory = new TravelMemoryApplicationFactory(
            databaseConnectionString,
            azurite.GetConnectionString(),
            Guid.NewGuid()))
        using (var otherClient = otherFactory.CreateClient())
        {
            var foreign = await otherClient.DeleteAsync($"/api/trips/{trip.Id}/photos/{photo.Id}");
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }

        var deleted = await client.DeleteAsync($"/api/trips/{trip.Id}/photos/{photo.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Empty((await GetTimelineAsync(client, trip.Id)).Items);
        Assert.Equal(0, await CountBlobsAsync(permanent));
        var repeated = await client.DeleteAsync($"/api/trips/{trip.Id}/photos/{photo.Id}");
        Assert.Equal(HttpStatusCode.NotFound, repeated.StatusCode);

        // The deleted photo no longer counts as a duplicate.
        var reimported = await ImportAndFinalizeAsync(client, trip.Id, "delete-me-again.jpg", jpeg);
        var item = Assert.Single(reimported.Items);
        Assert.Equal(nameof(PhotoImportItemOutcome.Imported), item.Outcome);
        Assert.Single((await GetTimelineAsync(client, trip.Id)).Items);
    }

    private async Task<PhotoImportBatchResponse> ImportAndFinalizeAsync(
        HttpClient client,
        Guid tripId,
        string fileName,
        byte[] content)
    {
        var batch = await CreateSingleFileBatchAsync(client, tripId, fileName, "image/jpeg", content);
        await UploadAndCompleteAsync(client, batch.Id, Assert.Single(batch.Items), content);
        await ProcessAllPendingJobsAsync();
        var finalize = await client.PostAsJsonAsync(
            $"/api/photo-imports/{batch.Id}/finalize",
            new FinalizePhotoImportRequest(0));
        Assert.Equal(HttpStatusCode.Accepted, finalize.StatusCode);
        await ProcessAllPendingJobsAsync();
        return await GetBatchAsync(client, batch.Id);
    }

    private static async Task<PhotoTimelineResponse> GetTimelineAsync(
        HttpClient client,
        Guid tripId) =>
        Assert.IsType<PhotoTimelineResponse>(
            await client.GetFromJsonAsync<PhotoTimelineResponse>($"/api/trips/{tripId}/photos"));

    [Fact]
    public async Task Deletes_a_trip_with_its_photos_imports_jobs_and_blobs()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var trip = await CreateTripAsync(client);
        await ImportAndFinalizeAsync(client, trip.Id, "kept.jpg", CreateOrientedJpeg());
        // A second import stays pending, with its original still in temporary storage.
        var heic = await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.heic"));
        var pendingBatch = await CreateSingleFileBatchAsync(
            client,
            trip.Id,
            "pending.heic",
            "image/heic",
            heic);
        await UploadAndCompleteAsync(client, pendingBatch.Id, Assert.Single(pendingBatch.Items), heic);
        var prefix = PhotoStorageNames.TripPrefix(OwnerId, trip.Id);
        Assert.Equal(3, await CountTripBlobsAsync(prefix));

        using (var otherFactory = new TravelMemoryApplicationFactory(
            databaseConnectionString,
            azurite.GetConnectionString(),
            Guid.NewGuid()))
        using (var otherClient = otherFactory.CreateClient())
        {
            var foreign = await otherClient.DeleteAsync($"/api/trips/{trip.Id}");
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }

        var deleted = await client.DeleteAsync($"/api/trips/{trip.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/trips/{trip.Id}")).StatusCode);
        Assert.Equal(0, await CountTripBlobsAsync(prefix));
        await AssertTripRowsGoneAsync(trip.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/trips/{trip.Id}")).StatusCode);
    }

    [Fact]
    public async Task Refuses_to_delete_a_trip_while_a_photo_is_processing()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var trip = await CreateTripAsync(client);
        var jpeg = CreateOrientedJpeg();
        var batch = await CreateSingleFileBatchAsync(client, trip.Id, "busy.jpg", "image/jpeg", jpeg);
        var item = Assert.Single(batch.Items);
        await UploadAndCompleteAsync(client, batch.Id, item, jpeg);
        await using (var context = CreateDbContext())
        {
            var job = await context.PhotoProcessingJobs.SingleAsync(value => value.ImportItemId == item.Id);
            Assert.True(job.TryStart(DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
        }

        var response = await client.DeleteAsync($"/api/trips/{trip.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/trips/{trip.Id}")).StatusCode);
    }

    [Fact]
    public async Task Completes_a_trip_deletion_that_stopped_halfway()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var trip = await CreateTripAsync(client);
        await ImportAndFinalizeAsync(client, trip.Id, "left-behind.jpg", CreateOrientedJpeg());
        // As if an earlier deletion hid the trip and then stopped before cleaning up.
        await using (var context = CreateDbContext())
        {
            var stored = await context.Trips.SingleAsync(value => value.Id == trip.Id);
            stored.MarkDeleting(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/trips/{trip.Id}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/trips/{trip.Id}/photos")).StatusCode);

        var deleted = await client.DeleteAsync($"/api/trips/{trip.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(0, await CountTripBlobsAsync(PhotoStorageNames.TripPrefix(OwnerId, trip.Id)));
        await AssertTripRowsGoneAsync(trip.Id);
    }

    private async Task<int> CountTripBlobsAsync(string prefix)
    {
        var blobService = new BlobServiceClient(azurite.GetConnectionString());
        var count = 0;
        foreach (var containerName in new[]
                 {
                     PhotoStorageNames.TemporaryContainer,
                     PhotoStorageNames.PermanentContainer,
                 })
        {
            await foreach (var _ in blobService
                               .GetBlobContainerClient(containerName)
                               .GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, CancellationToken.None))
            {
                count++;
            }
        }

        return count;
    }

    private async Task AssertTripRowsGoneAsync(Guid tripId)
    {
        await using var context = CreateDbContext();
        Assert.False(await context.Trips.IgnoreQueryFilters().AnyAsync(value => value.Id == tripId));
        Assert.False(await context.Photos.AnyAsync(value => value.TripId == tripId));
        Assert.False(await context.PhotoImportBatches.AnyAsync(value => value.TripId == tripId));
        Assert.False(await context.PhotoImportItems.AnyAsync(value => value.TripId == tripId));
        Assert.False(await context.PhotoProcessingJobs.AnyAsync(
            job => !context.PhotoImportBatches.Any(batch => batch.Id == job.ImportBatchId)));
    }

    [Fact]
    public async Task Removes_its_derivatives_when_the_trip_is_deleted_while_a_photo_processes()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var trip = await CreateTripAsync(client);
        var jpeg = CreateOrientedJpeg();
        var batch = await CreateSingleFileBatchAsync(client, trip.Id, "racing.jpg", "image/jpeg", jpeg);
        await UploadAndCompleteAsync(client, batch.Id, Assert.Single(batch.Items), jpeg);
        await ProcessAllPendingJobsAsync();
        var finalize = await client.PostAsJsonAsync(
            $"/api/photo-imports/{batch.Id}/finalize",
            new FinalizePhotoImportRequest(0));
        Assert.Equal(HttpStatusCode.Accepted, finalize.StatusCode);
        var processJobId = await GetProcessJobIdAsync(Assert.Single(batch.Items).Id);

        // The trip's rows are deleted, as DeleteTrip does, after the worker uploaded the
        // derivatives and just before it saves the photo.
        await using (var context = CreateDbContext(
            new BeforePhotoInsertInterceptor(async () =>
            {
                await using var deletion = CreateDbContext();
                await deletion.Photos.Where(photo => photo.TripId == trip.Id).ExecuteDeleteAsync();
                await deletion.PhotoImportBatches
                    .Where(value => value.TripId == trip.Id)
                    .ExecuteDeleteAsync();
            })))
        {
            var processor = new PhotoJobProcessor(
                context,
                new BlobServiceClient(azurite.GetConnectionString()),
                new PhotoImageProcessor(),
                TimeProvider.System,
                NullLogger<PhotoJobProcessor>.Instance);
            await processor.ProcessAsync(processJobId, CancellationToken.None);
        }

        var derivatives = new BlobServiceClient(azurite.GetConnectionString())
            .GetBlobContainerClient(PhotoStorageNames.PermanentContainer);
        Assert.Equal(0, await CountBlobsAsync(derivatives));
    }

    [Fact]
    public async Task Refuses_new_import_work_for_a_trip_that_is_being_deleted()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var trip = await CreateTripAsync(client);
        var jpeg = CreateOrientedJpeg();
        var batch = await CreateSingleFileBatchAsync(client, trip.Id, "late.jpg", "image/jpeg", jpeg);
        var item = Assert.Single(batch.Items);
        await UploadAndCompleteAsync(client, batch.Id, item, jpeg);
        await ProcessAllPendingJobsAsync();
        await using (var context = CreateDbContext())
        {
            var stored = await context.Trips.SingleAsync(value => value.Id == trip.Id);
            stored.MarkDeleting(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        var finalize = await client.PostAsJsonAsync(
            $"/api/photo-imports/{batch.Id}/finalize",
            new FinalizePhotoImportRequest(0));

        Assert.Equal(HttpStatusCode.NotFound, finalize.StatusCode);
        await using var verification = CreateDbContext();
        Assert.False(await verification.PhotoProcessingJobs.AnyAsync(
            job => job.ImportItemId == item.Id && job.Kind == PhotoProcessingJobKind.Process));
    }

    private TravelMemoryApplicationFactory CreateFactory() =>
        new(databaseConnectionString, azurite.GetConnectionString(), OwnerId);

    private TravelMemoryDbContext CreateDbContext(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<TravelMemoryDbContext>()
            .UseSqlServer(databaseConnectionString)
            .AddInterceptors(interceptors)
            .Options;
        return new TravelMemoryDbContext(options);
    }

    // Each attempt starts the job and then loses its worker, like a process that is killed
    // mid-job, so the job is only recovered by the abandoned-job timeout.
    private async Task SimulateCrashedAttemptsAsync(
        Guid itemId,
        int attempts,
        DateTimeOffset firstStartedAt)
    {
        await using var context = CreateDbContext();
        var job = await context.PhotoProcessingJobs.SingleAsync(
            value => value.ImportItemId == itemId);
        var item = await context.PhotoImportItems.SingleAsync(value => value.Id == itemId);
        var startedAt = firstStartedAt;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            if (attempt > 1)
            {
                startedAt = startedAt.AddMinutes(11);
                Assert.True(job.RecoverIfAbandoned(startedAt, TimeSpan.FromMinutes(10)));
            }

            Assert.True(job.TryStart(startedAt));
        }

        item.MarkAnalyzing(startedAt);
        await context.SaveChangesAsync();
    }

    private async Task<Guid> GetProcessJobIdAsync(Guid itemId)
    {
        await using var context = CreateDbContext();
        return (await context.PhotoProcessingJobs.SingleAsync(
            job =>
                job.ImportItemId == itemId
                && job.Kind == PhotoProcessingJobKind.Process)).Id;
    }

    private ServiceProvider CreateWorkerServices(TimeProvider? timeProvider = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TravelMemoryDbContext>(options =>
            options.UseSqlServer(databaseConnectionString));
        services.AddSingleton(new BlobServiceClient(azurite.GetConnectionString()));
        services.AddSingleton(new QueueServiceClient(azurite.GetConnectionString()));
        services.AddSingleton(timeProvider ?? TimeProvider.System);
        services.AddSingleton<PhotoImageProcessor>();
        services.AddScoped<PhotoJobProcessor>();
        services.AddSingleton<PhotoMaintenance>();
        services.AddSingleton<PhotoQueueWorker>();
        return services.BuildServiceProvider();
    }

    private static async Task<PhotoImportItemResponse> WaitForSingleItemAsync(
        HttpClient client,
        Guid batchId,
        Func<PhotoImportItemResponse, bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            var item = Assert.Single((await GetBatchAsync(client, batchId)).Items);
            if (predicate(item))
            {
                return item;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                Assert.Fail(
                    $"Item {item.FileName} is still {item.State} ({item.ErrorCode}) after 30 seconds.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }

    private async Task ProcessAllPendingJobsAsync()
    {
        for (var iteration = 0; iteration < 20; iteration++)
        {
            List<Guid> jobIds;
            await using (var queryContext = CreateDbContext())
            {
                var now = DateTimeOffset.UtcNow;
                jobIds = await queryContext.PhotoProcessingJobs
                    .Where(job =>
                        job.State == PhotoProcessingJobState.Pending
                        && job.AvailableAtUtc <= now)
                    .OrderBy(job => job.CreatedAtUtc)
                    .Select(job => job.Id)
                    .ToListAsync();
            }

            if (jobIds.Count == 0)
            {
                return;
            }

            foreach (var jobId in jobIds)
            {
                await ProcessJobAsync(jobId, TimeProvider.System);
            }
        }

        throw new InvalidOperationException("Photo jobs did not reach a terminal state.");
    }

    private async Task ProcessJobAsync(Guid jobId, TimeProvider timeProvider)
    {
        await using var context = CreateDbContext();
        var processor = new PhotoJobProcessor(
            context,
            new BlobServiceClient(azurite.GetConnectionString()),
            new PhotoImageProcessor(),
            timeProvider,
            NullLogger<PhotoJobProcessor>.Instance);
        await processor.ProcessAsync(jobId, CancellationToken.None);
    }

    private static async Task<TripResponse> CreateTripAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/trips/",
            new CreateTripRequest(
                "Photo trip",
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 20)));
        response.EnsureSuccessStatusCode();
        return Assert.IsType<TripResponse>(
            await response.Content.ReadFromJsonAsync<TripResponse>());
    }

    private static async Task<PhotoImportBatchResponse> CreateSingleFileBatchAsync(
        HttpClient client,
        Guid tripId,
        string fileName,
        string contentType,
        byte[] content)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/trips/{tripId}/photo-imports",
            new CreatePhotoImportRequest(
                Guid.NewGuid(),
                [CreateFile(fileName, contentType, content)]));
        response.EnsureSuccessStatusCode();
        return Assert.IsType<PhotoImportBatchResponse>(
            await response.Content.ReadFromJsonAsync<PhotoImportBatchResponse>());
    }

    private static CreatePhotoImportFileRequest CreateFile(
        string fileName,
        string contentType,
        byte[] content) =>
        new(
            Convert.ToHexStringLower(SHA256.HashData(content)),
            fileName,
            contentType,
            content.LongLength);

    private static async Task UploadAndCompleteAsync(
        HttpClient apiClient,
        Guid batchId,
        PhotoImportItemResponse item,
        byte[] content)
    {
        await UploadAsync(item, content);
        var completionResponse = await apiClient.PostAsync(
            $"/api/photo-imports/{batchId}/items/{item.Id}/complete-upload",
            content: null);
        Assert.Equal(HttpStatusCode.Accepted, completionResponse.StatusCode);
    }

    private static async Task UploadAsync(PhotoImportItemResponse item, byte[] content)
    {
        Assert.NotNull(item.UploadUrl);
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, item.UploadUrl);
        uploadRequest.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");
        uploadRequest.Content = new ByteArrayContent(content);
        uploadRequest.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(item.ContentType);
        using var uploadClient = new HttpClient();
        var uploadResponse = await uploadClient.SendAsync(uploadRequest);
        uploadResponse.EnsureSuccessStatusCode();
    }

    private static async Task<PhotoImportBatchResponse> GetBatchAsync(
        HttpClient client,
        Guid batchId) =>
        Assert.IsType<PhotoImportBatchResponse>(
            await client.GetFromJsonAsync<PhotoImportBatchResponse>(
                $"/api/photo-imports/{batchId}"));

    private static void AssertLeastPrivilegeUploadGrant(PhotoImportItemResponse item)
    {
        Assert.NotNull(item.UploadUrl);
        Assert.NotNull(item.UploadExpiresAtUtc);
        var permissions = QueryHelpers.ParseQuery(item.UploadUrl.Query)["sp"].ToString();
        Assert.Contains('c', permissions);
        Assert.Contains('w', permissions);
        Assert.DoesNotContain('r', permissions);
        Assert.DoesNotContain('d', permissions);
        Assert.InRange(
            item.UploadExpiresAtUtc.Value - DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(13),
            TimeSpan.FromMinutes(16));
    }

    private static byte[] CreateOrientedJpeg()
        => File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "oriented.jpg"));

    private static async Task<int> CountQueueMessagesAsync(QueueClient queue) =>
        (await queue.PeekMessagesAsync(maxMessages: 32)).Value.Length;

    private static async Task<int> CountBlobsAsync(BlobContainerClient container)
    {
        var count = 0;
        await foreach (var _ in container.GetBlobsAsync())
        {
            count++;
        }

        return count;
    }
}

internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}

internal sealed class BeforePhotoInsertInterceptor(Func<Task> action) : SaveChangesInterceptor
{
    private bool triggered;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (!triggered
            && eventData.Context!.ChangeTracker
                .Entries<Photo>()
                .Any(entry => entry.State == EntityState.Added))
        {
            triggered = true;
            await action();
        }

        return result;
    }
}
