using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.EntityFrameworkCore;
using TravelMemory.Domain.Photos;
using TravelMemory.Persistence.Data;
using TravelMemory.Persistence.Photos;

namespace TravelMemory.Worker;

internal sealed class PhotoQueueWorker(
    IServiceScopeFactory scopeFactory,
    BlobServiceClient blobServiceClient,
    QueueServiceClient queueServiceClient,
    TimeProvider timeProvider,
    ILogger<PhotoQueueWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan EmptyQueueDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan VisibilityTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RedispatchInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AbandonedJobTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan IncompleteUploadRetention = TimeSpan.FromHours(24);
    private const int MaximumConcurrency = 4;
    private DateTimeOffset nextDispatchAtUtc = DateTimeOffset.MinValue;

    private QueueClient Queue =>
        queueServiceClient.GetQueueClient(PhotoStorageNames.ProcessingQueue);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InitializeStorageAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await DispatchPendingJobsAsync(stoppingToken);
            var messages = (await Queue.ReceiveMessagesAsync(
                maxMessages: 8,
                visibilityTimeout: VisibilityTimeout,
                cancellationToken: stoppingToken)).Value;

            if (messages.Length == 0)
            {
                await Task.Delay(EmptyQueueDelay, stoppingToken);
                continue;
            }

            await Parallel.ForEachAsync(
                messages,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaximumConcurrency,
                    CancellationToken = stoppingToken,
                },
                ProcessMessageAsync);
        }
    }

    private async Task ExpireIncompleteUploadsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TravelMemoryDbContext>();
        var now = timeProvider.GetUtcNow();
        var cutoff = now.Subtract(IncompleteUploadRetention);
        var items = await dbContext.PhotoImportItems
            .Where(item =>
                item.State == PhotoImportItemState.AwaitingUpload
                && item.CreatedAtUtc <= cutoff)
            .OrderBy(item => item.CreatedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            await blobServiceClient
                .GetBlobContainerClient(PhotoStorageNames.TemporaryContainer)
                .GetBlobClient(item.TemporaryBlobName)
                .DeleteIfExistsAsync(cancellationToken: cancellationToken);
            item.MarkFailed(
                "upload_expired",
                "The upload was not completed within 24 hours. Select the file again in a new import.",
                now);
            item.MarkFailedOriginalDeleted(now);
        }

        var batchIds = items.Select(item => item.ImportBatchId).Distinct().ToArray();
        foreach (var batchId in batchIds)
        {
            var batch = await dbContext.PhotoImportBatches.SingleAsync(
                value => value.Id == batchId,
                cancellationToken);
            var batchItems = await dbContext.PhotoImportItems
                .Where(item => item.ImportBatchId == batchId)
                .ToListAsync(cancellationToken);
            batch.SetState(PhotoImportBatchStateCalculator.Calculate(batch, batchItems), now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async ValueTask ProcessMessageAsync(
        QueueMessage message,
        CancellationToken cancellationToken)
    {
        PhotoQueueMessage? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PhotoQueueMessage>(message.MessageText);
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "Discarding malformed photo queue message {MessageId}.", message.MessageId);
            await Queue.DeleteMessageAsync(
                message.MessageId,
                message.PopReceipt,
                cancellationToken);
            return;
        }

        if (payload is null || payload.JobId == Guid.Empty)
        {
            logger.LogError("Discarding photo queue message {MessageId} without a job id.", message.MessageId);
            await Queue.DeleteMessageAsync(
                message.MessageId,
                message.PopReceipt,
                cancellationToken);
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<PhotoJobProcessor>();
            await processor.ProcessAsync(payload.JobId, cancellationToken);
            await Queue.DeleteMessageAsync(
                message.MessageId,
                message.PopReceipt,
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogInformation(
                exception,
                "Photo job {JobId} was claimed by another worker.",
                payload.JobId);
            await Queue.DeleteMessageAsync(
                message.MessageId,
                message.PopReceipt,
                cancellationToken);
        }
        catch (RequestFailedException exception) when (
            exception.Status is 408 or 429 or >= 500)
        {
            logger.LogWarning(
                exception,
                "Transient storage error while processing photo job {JobId}; the queue message remains visible for retry.",
                payload.JobId);
        }
    }

    private async Task DispatchPendingJobsAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (now < nextDispatchAtUtc)
        {
            return;
        }

        nextDispatchAtUtc = now.Add(RedispatchInterval);
        await ExpireIncompleteUploadsAsync(cancellationToken);

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TravelMemoryDbContext>();
        var abandonedJobs = await dbContext.PhotoProcessingJobs
            .Where(job =>
                job.State == PhotoProcessingJobState.Processing
                && job.UpdatedAtUtc <= now.Subtract(AbandonedJobTimeout))
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var job in abandonedJobs)
        {
            job.RecoverIfAbandoned(now, AbandonedJobTimeout);
        }

        var activeBatches = await dbContext.PhotoImportBatches
            .Where(batch =>
                batch.State != PhotoImportBatchState.Completed
                && batch.State != PhotoImportBatchState.CompletedWithErrors)
            .OrderBy(batch => batch.UpdatedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var batch in activeBatches)
        {
            var items = await dbContext.PhotoImportItems
                .Where(item => item.ImportBatchId == batch.Id)
                .ToListAsync(cancellationToken);
            batch.SetState(PhotoImportBatchStateCalculator.Calculate(batch, items), now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var jobs = await dbContext.PhotoProcessingJobs
            .Where(job =>
                job.State == PhotoProcessingJobState.Pending
                && job.AvailableAtUtc <= now)
            .OrderBy(job => job.AvailableAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var job in jobs)
        {
            await Queue.SendMessageAsync(
                JsonSerializer.Serialize(new PhotoQueueMessage(job.Id)),
                cancellationToken);
        }
    }

    private async Task InitializeStorageAsync(CancellationToken cancellationToken)
    {
        await blobServiceClient
            .GetBlobContainerClient(PhotoStorageNames.TemporaryContainer)
            .CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        await blobServiceClient
            .GetBlobContainerClient(PhotoStorageNames.PermanentContainer)
            .CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        await Queue.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
    }
}
