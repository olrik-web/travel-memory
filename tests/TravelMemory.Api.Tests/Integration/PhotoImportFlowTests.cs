using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using TravelMemory.Api.Features.PhotoImports;
using TravelMemory.Api.Features.Trips;
using TravelMemory.Domain.Photos;
using TravelMemory.Persistence.Data;
using TravelMemory.Persistence.Photos;
using TravelMemory.Worker;

namespace TravelMemory.Api.Tests.Integration;

[Collection(ContainerTestCollection.Name)]
public sealed class PhotoImportFlowTests : IAsyncLifetime
{
    private static readonly Guid OwnerId =
        Guid.Parse("78cb7c99-b0f4-42a8-acbb-45b800cd9fb8");

    private readonly MsSqlContainer sqlServer =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest")
            .Build();
    private readonly AzuriteContainer azurite =
        new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.35.0")
            .WithCommand("--skipApiVersionCheck")
            .Build();

    public Task InitializeAsync() =>
        Task.WhenAll(sqlServer.StartAsync(), azurite.StartAsync());

    public async Task DisposeAsync()
    {
        await sqlServer.DisposeAsync();
        await azurite.DisposeAsync();
    }

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
        Assert.Contains("beskadiget", failedItem.ErrorMessage);
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

    private TravelMemoryApplicationFactory CreateFactory() =>
        new(sqlServer.GetConnectionString(), azurite.GetConnectionString(), OwnerId);

    private TravelMemoryDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TravelMemoryDbContext>()
            .UseSqlServer(sqlServer.GetConnectionString())
            .Options;
        return new TravelMemoryDbContext(options);
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
                await using var context = CreateDbContext();
                var processor = new PhotoJobProcessor(
                    context,
                    new BlobServiceClient(azurite.GetConnectionString()),
                    new PhotoImageProcessor(),
                    TimeProvider.System,
                    NullLogger<PhotoJobProcessor>.Instance);
                await processor.ProcessAsync(jobId, CancellationToken.None);
            }
        }

        throw new InvalidOperationException("Photo jobs did not reach a terminal state.");
    }

    private static async Task<TripResponse> CreateTripAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/trips/",
            new CreateTripRequest(
                "Fotorejse",
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
        Assert.NotNull(item.UploadUrl);
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, item.UploadUrl);
        uploadRequest.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");
        uploadRequest.Content = new ByteArrayContent(content);
        uploadRequest.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(item.ContentType);
        using var uploadClient = new HttpClient();
        var uploadResponse = await uploadClient.SendAsync(uploadRequest);
        uploadResponse.EnsureSuccessStatusCode();

        var completionResponse = await apiClient.PostAsync(
            $"/api/photo-imports/{batchId}/items/{item.Id}/complete-upload",
            content: null);
        Assert.Equal(HttpStatusCode.Accepted, completionResponse.StatusCode);
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
