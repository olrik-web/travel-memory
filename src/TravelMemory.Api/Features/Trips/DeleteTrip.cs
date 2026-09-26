using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TravelMemory.Api.Auth;
using TravelMemory.Api.Features.PhotoImports;
using TravelMemory.Domain.Photos;
using TravelMemory.Persistence.Data;

namespace TravelMemory.Api.Features.Trips;

// Deletes a trip with its photos, imports, jobs, and blobs. Every step can be repeated, so
// a deletion that stops halfway leaves the trip hidden and is completed by deleting again.
internal static class DeleteTrip
{
    private const string ProcessingConflict =
        "Photos of this trip are still being processed. Try again when the import has finished.";

    public static async Task<Results<NoContent, NotFound, Conflict<string>>> HandleAsync(
        Guid id,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        PhotoStorage storage,
        TravelMemoryDbContext dbContext,
        CancellationToken cancellationToken)
    {
        // Past the query filter, so a deletion that stopped halfway can be resumed.
        var trip = await dbContext.Trips
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                value => value.Id == id && value.OwnerId == currentUser.OwnerId,
                cancellationToken);
        if (trip is null)
        {
            return TypedResults.NotFound();
        }

        // A worker that is processing a photo would upload its derivatives after the blobs
        // below are gone, so wait for it, unless it has been silent long enough to be dead.
        var now = timeProvider.GetUtcNow();
        var jobs = await dbContext.PhotoProcessingJobs
            .Where(job => dbContext.PhotoImportBatches.Any(
                batch => batch.Id == job.ImportBatchId && batch.TripId == id))
            .ToListAsync(cancellationToken);
        if (jobs.Any(job =>
                job.State == PhotoProcessingJobState.Processing
                && !job.IsAbandoned(now, PhotoProcessingJob.AbandonedAfter)))
        {
            return TypedResults.Conflict(ProcessingConflict);
        }

        // Hiding the trip and removing its jobs together stops all further work on it: a
        // queued message finds no job, and the job rowversions make this fail if a worker
        // started one of them since they were read.
        trip.MarkDeleting(now);
        dbContext.PhotoProcessingJobs.RemoveRange(jobs);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.Conflict(ProcessingConflict);
        }

        // Blobs before rows: a failure in between leaves a hidden trip to delete again,
        // never blobs without a row that leads to them.
        await storage.DeleteTripBlobsAsync(currentUser.OwnerId, id, cancellationToken);

        // Photos first, because they restrict deleting their batches and items; deleting a
        // batch cascades to its items and their jobs.
        await dbContext.Photos
            .Where(photo => photo.TripId == id)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.PhotoImportBatches
            .Where(batch => batch.TripId == id)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.Trips
            .IgnoreQueryFilters()
            .Where(value => value.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

        return TypedResults.NoContent();
    }
}
