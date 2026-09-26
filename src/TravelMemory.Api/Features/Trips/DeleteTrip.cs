using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
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
    private const string BusyConflict =
        "The trip changed while it was being deleted. Delete it again to finish.";

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

        // Hiding the trip and removing its jobs together stops the work that is known: a
        // queued message finds no job, and the job rowversions make this fail if a worker
        // started one of them since they were read. Endpoints refuse new work for a hidden
        // trip, and anything that still slips in is handled by the order below.
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

        // Rows, then blobs, then the trip itself. Deleting the rows first takes the item and
        // job away from any worker still busy with this trip, so it cannot save a photo and
        // removes the derivatives it uploaded. The blob sweep goes by name prefix and needs
        // no rows, and the trip row stays until it succeeds, so a failure anywhere leaves a
        // hidden trip that deleting again finishes. Photos go first because they restrict
        // their batches and items; deleting a batch cascades to its items and jobs.
        await dbContext.Photos
            .Where(photo => photo.TripId == id)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.PhotoImportBatches
            .Where(batch => batch.TripId == id)
            .ExecuteDeleteAsync(cancellationToken);
        await storage.DeleteTripBlobsAsync(currentUser.OwnerId, id, cancellationToken);
        try
        {
            await dbContext.Trips
                .IgnoreQueryFilters()
                .Where(value => value.Id == id)
                .ExecuteDeleteAsync(cancellationToken);
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            // A batch or photo was added after the rows above were deleted and still points
            // to the trip. The next attempt deletes it too.
            return TypedResults.Conflict(BusyConflict);
        }

        return TypedResults.NoContent();
    }
}
