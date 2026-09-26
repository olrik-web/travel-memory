using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.EntityFrameworkCore;
using TravelMemory.Domain.Photos;
using TravelMemory.Persistence.Data;
using TravelMemory.Persistence.Photos;

namespace TravelMemory.Worker;

// Housekeeping that must happen even when no queue message arrives: expiring uploads that
// were never completed, recovering jobs whose worker died, and sending messages for pending
// jobs that have none. The long-running worker runs it every few seconds; in Azure a
// scheduled job also runs it once a day while the worker is scaled to zero.
internal sealed class PhotoMaintenance(
    IServiceScopeFactory scopeFactory,
    BlobServiceClient blobServiceClient,
    QueueServiceClient queueServiceClient,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan LostMessageTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AbandonedJobTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan IncompleteUploadRetention = TimeSpan.FromHours(24);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await ExpireIncompleteUploadsAsync(now, cancellationToken);

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TravelMemoryDbContext>();
        var abandonedJobIds = await dbContext.PhotoProcessingJobs
            .Where(job =>
                job.State == PhotoProcessingJobState.Processing
                && job.UpdatedAtUtc <= now.Subtract(AbandonedJobTimeout))
            .Select(job => job.Id)
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var jobId in abandonedJobIds)
        {
            await using var jobScope = scopeFactory.CreateAsyncScope();
            await jobScope.ServiceProvider
                .GetRequiredService<PhotoJobProcessor>()
                .RecoverAbandonedAsync(jobId, AbandonedJobTimeout, cancellationToken);
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

        // A pending job needs a message if none was sent since it last became available
        // (worker-created jobs, retries after a backoff, recovered jobs), or if the last one
        // appears to be lost. A job waiting behind a backlog already has a message queued.
        var lostBefore = now.Subtract(LostMessageTimeout);
        var jobs = await dbContext.PhotoProcessingJobs
            .Where(job =>
                job.State == PhotoProcessingJobState.Pending
                && job.AvailableAtUtc <= now
                && (job.LastDispatchedAtUtc == null
                    || job.LastDispatchedAtUtc < job.AvailableAtUtc
                    || job.LastDispatchedAtUtc <= lostBefore))
            .OrderBy(job => job.AvailableAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        // Saving before sending means a failed send is only retried after the lost-message
        // timeout, but a message is never sent for a job the database does not know was
        // dispatched.
        foreach (var job in jobs)
        {
            job.MarkDispatched(now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var job in jobs)
        {
            await queueServiceClient
                .GetQueueClient(PhotoStorageNames.ProcessingQueue)
                .SendMessageAsync(
                    JsonSerializer.Serialize(new PhotoQueueMessage(job.Id, job.TraceParent)),
                    cancellationToken);
        }
    }

    private async Task ExpireIncompleteUploadsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TravelMemoryDbContext>();
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
}
