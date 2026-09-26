using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TravelMemory.Api.Auth;
using TravelMemory.Domain.Photos;
using TravelMemory.Persistence.Data;
using TravelMemory.Persistence.Photos;

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
            return AcceptedBatch(batch, items, storage);
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
        var job = PhotoProcessingJob.Create(
            item,
            PhotoProcessingJobKind.Analyze,
            now,
            PhotoJobTracing.CurrentTraceParent);
        job.MarkDispatched(now);
        dbContext.PhotoProcessingJobs.Add(job);
        batch.SetState(PhotoImportBatchStateCalculator.Calculate(batch, items), now);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception is DbUpdateConcurrencyException
            || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // A concurrent call for the same item, such as a client retry or a double
            // click, completed it first (item rowversion or the unique Analyze job) and
            // dispatched its job, so answer like the idempotent path above.
            dbContext.ChangeTracker.Clear();
            batch = await dbContext.PhotoImportBatches.SingleAsync(
                value => value.Id == batchId,
                cancellationToken);
            items = await dbContext.PhotoImportItems
                .Where(value => value.ImportBatchId == batchId)
                .ToListAsync(cancellationToken);
            return AcceptedBatch(batch, items, storage);
        }

        if (!await dispatcher.TryDispatchAsync(job, cancellationToken))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "The photo is saved, but the processing queue is temporarily unavailable.",
                detail: "The photo will be queued for analysis automatically once the queue is available.");
        }

        return AcceptedBatch(batch, items, storage);
    }

    private static Accepted<PhotoImportBatchResponse> AcceptedBatch(
        PhotoImportBatch batch,
        List<PhotoImportItem> items,
        PhotoStorage storage) =>
        TypedResults.Accepted(
            $"/api/photo-imports/{batch.Id}",
            PhotoImportResponseFactory.Create(
                batch,
                items,
                storage,
                uploadGrantSigner: null));
}
