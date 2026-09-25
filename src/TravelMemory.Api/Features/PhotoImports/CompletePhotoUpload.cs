using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TravelMemory.Api.Auth;
using TravelMemory.Domain.Photos;
using TravelMemory.Persistence.Data;

namespace TravelMemory.Api.Features.PhotoImports;

internal static class CompletePhotoUpload
{
    public static async Task<Results<
            Accepted<PhotoImportBatchResponse>,
            NotFound,
            ValidationProblem,
            ProblemHttpResult>>
        HandleAsync(
            Guid batchId,
            Guid itemId,
            ICurrentUser currentUser,
            TimeProvider timeProvider,
            PhotoStorage storage,
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

        var batch = await dbContext.PhotoImportBatches.SingleAsync(
            value => value.Id == batchId && value.OwnerId == currentUser.OwnerId,
            cancellationToken);
        var items = await dbContext.PhotoImportItems
            .Where(value => value.ImportBatchId == batchId)
            .ToListAsync(cancellationToken);

        if (item.State != PhotoImportItemState.AwaitingUpload)
        {
            return TypedResults.Accepted(
                $"/api/photo-imports/{batch.Id}",
                PhotoImportResponseFactory.Create(
                    batch,
                    items,
                    storage,
                    includeUploadGrants: false));
        }

        if (!await storage.VerifyUploadAsync(
                item.TemporaryBlobName,
                item.ExpectedSizeBytes,
                cancellationToken))
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["file"] =
                    [
                        "The upload is missing or has the wrong size. Upload the file again.",
                    ],
                });
        }

        var now = timeProvider.GetUtcNow();
        item.QueueForAnalysis(now);
        var job = PhotoProcessingJob.Create(item, PhotoProcessingJobKind.Analyze, now);
        job.MarkDispatched(now);
        dbContext.PhotoProcessingJobs.Add(job);
        batch.SetState(PhotoImportBatchStateCalculator.Calculate(batch, items), now);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (!await dispatcher.TryDispatchAsync(job, cancellationToken))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "The photo is saved, but the processing queue is temporarily unavailable.",
                detail: "Try completing the upload again. No new job is created.");
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
