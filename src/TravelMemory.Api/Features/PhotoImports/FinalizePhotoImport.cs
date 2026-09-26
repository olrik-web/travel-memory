using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TravelMemory.Api.Auth;
using TravelMemory.Domain.Photos;
using TravelMemory.Persistence.Data;
using TravelMemory.Persistence.Photos;

namespace TravelMemory.Api.Features.PhotoImports;

internal static class FinalizePhotoImport
{
    public static async Task<Results<
            Accepted<PhotoImportBatchResponse>,
            ValidationProblem,
            NotFound,
            Conflict<string>,
            ProblemHttpResult>>
        HandleAsync(
            Guid batchId,
            FinalizePhotoImportRequest request,
            ICurrentUser currentUser,
            TimeProvider timeProvider,
            PhotoStorage storage,
            PhotoJobDispatcher dispatcher,
            TravelMemoryDbContext dbContext,
            CancellationToken cancellationToken)
    {
        if (request.TimeAdjustmentMinutes is
            < -PhotoImportBatch.MaximumAdjustmentMinutes
            or > PhotoImportBatch.MaximumAdjustmentMinutes)
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["timeAdjustmentMinutes"] =
                    [
                        "The time adjustment must be between -24 and +24 hours.",
                    ],
                });
        }

        var batch = await dbContext.PhotoImportBatches.SingleOrDefaultAsync(
            value => value.Id == batchId && value.OwnerId == currentUser.OwnerId,
            cancellationToken);
        if (batch is null)
        {
            return TypedResults.NotFound();
        }

        var items = await dbContext.PhotoImportItems
            .Where(item => item.ImportBatchId == batch.Id)
            .ToListAsync(cancellationToken);

        // A repeated finalize with the same adjustment is a retry after a queue outage:
        // resend the pending jobs instead of creating new ones.
        if (batch.TimeAdjustmentMinutes is not null)
        {
            if (batch.TimeAdjustmentMinutes != request.TimeAdjustmentMinutes)
            {
                return TypedResults.Conflict(
                    "The batch is already finalized with a different time adjustment.");
            }

            var pendingJobs = await dbContext.PhotoProcessingJobs
                .Where(job =>
                    job.ImportBatchId == batch.Id
                    && job.State == PhotoProcessingJobState.Pending)
                .ToListAsync(cancellationToken);

            if (!await dispatcher.TryDispatchAllAsync(pendingJobs, cancellationToken))
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "The processing queue is temporarily unavailable.");
            }

            return TypedResults.Accepted(
                $"/api/photo-imports/{batch.Id}",
                PhotoImportResponseFactory.Create(
                    batch,
                    items,
                    storage,
                    includeUploadGrants: false));
        }

        if (batch.State != PhotoImportBatchState.ReadyForReview)
        {
            return TypedResults.Conflict(
                "The batch must finish analysis before the time adjustment can be locked.");
        }

        var now = timeProvider.GetUtcNow();
        batch.SetTimeAdjustment(request.TimeAdjustmentMinutes, now);
        var jobs = new List<PhotoProcessingJob>();
        foreach (var item in items.Where(
                     item => item.State == PhotoImportItemState.ReadyForReview))
        {
            item.QueueForProcessing(now);
            var job = PhotoProcessingJob.Create(
                item,
                PhotoProcessingJobKind.Process,
                now,
                PhotoJobTracing.CurrentTraceParent);
            job.MarkDispatched(now);
            jobs.Add(job);
            dbContext.PhotoProcessingJobs.Add(job);
        }

        batch.SetState(PhotoImportBatchStateCalculator.Calculate(batch, items), now);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (!await dispatcher.TryDispatchAllAsync(jobs, cancellationToken))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "The batch is saved, but the processing queue is temporarily unavailable.",
                detail: "Finalize again to resend the same idempotent jobs.");
        }

        return TypedResults.Accepted(
            $"/api/photo-imports/{batch.Id}",
            PhotoImportResponseFactory.Create(
                batch,
                items,
                storage,
                includeUploadGrants: false));
    }
}
