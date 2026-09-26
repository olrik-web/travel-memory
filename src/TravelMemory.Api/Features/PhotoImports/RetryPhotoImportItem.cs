using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TravelMemory.Api.Auth;
using TravelMemory.Domain.Photos;
using TravelMemory.Persistence.Data;
using TravelMemory.Persistence.Photos;

namespace TravelMemory.Api.Features.PhotoImports;

internal static class RetryPhotoImportItem
{
    public static async Task<Results<Accepted, NotFound, Conflict<string>, ProblemHttpResult>>
        HandleAsync(
            Guid batchId,
            Guid itemId,
            ICurrentUser currentUser,
            TimeProvider timeProvider,
            PhotoJobDispatcher dispatcher,
            TravelMemoryDbContext dbContext,
            CancellationToken cancellationToken)
    {
        var item = await dbContext.PhotoImportItems.SingleOrDefaultAsync(
            value =>
                value.Id == itemId
                && value.ImportBatchId == batchId
                && value.OwnerId == currentUser.OwnerId,
            cancellationToken);
        if (item is null)
        {
            return TypedResults.NotFound();
        }

        if (!PhotoImportResponseFactory.CanRetry(item))
        {
            return TypedResults.Conflict(
                "This failure cannot be retried. Select the file again in a new import.");
        }

        var job = await dbContext.PhotoProcessingJobs
            .Where(value =>
                value.ImportItemId == item.Id
                && value.State == PhotoProcessingJobState.Failed)
            .OrderByDescending(value => value.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (job is null)
        {
            return TypedResults.Conflict("There is no failed job for this file.");
        }

        var batch = await dbContext.PhotoImportBatches.SingleAsync(
            value => value.Id == batchId,
            cancellationToken);
        // A trip that is being deleted accepts no new work (see DeleteTrip).
        if (!await dbContext.Trips.AnyAsync(trip => trip.Id == batch.TripId, cancellationToken))
        {
            return TypedResults.NotFound();
        }
        var allItems = await dbContext.PhotoImportItems
            .Where(value => value.ImportBatchId == batchId)
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        job.ResetForManualRetry(now, PhotoJobTracing.CurrentTraceParent);
        job.MarkDispatched(now);

        // The retained original is needed again, so cancel its scheduled expiry.
        var expirationJobs = await dbContext.PhotoProcessingJobs
            .Where(value =>
                value.ImportItemId == item.Id
                && value.Kind == PhotoProcessingJobKind.ExpireFailedOriginal
                && value.State == PhotoProcessingJobState.Pending)
            .ToListAsync(cancellationToken);
        foreach (var expirationJob in expirationJobs)
        {
            expirationJob.Succeed(now);
        }

        item.ClearFailedOriginalRetention();
        if (job.Kind == PhotoProcessingJobKind.Cleanup)
        {
            item.MarkCleanupPending(item.Outcome, now, now);
        }
        else if (job.Kind == PhotoProcessingJobKind.Analyze)
        {
            item.QueueForAnalysis(now);
        }
        else
        {
            item.QueueForProcessing(now);
        }

        batch.SetState(PhotoImportBatchStateCalculator.Calculate(batch, allItems), now);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (!await dispatcher.TryDispatchAsync(job, cancellationToken))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "The processing queue is temporarily unavailable.");
        }

        return TypedResults.Accepted($"/api/photo-imports/{batchId}");
    }
}
