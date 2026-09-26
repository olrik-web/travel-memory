using Azure;
using Microsoft.EntityFrameworkCore;
using TravelMemory.Domain.Photos;
using TravelMemory.Persistence.Data;

namespace TravelMemory.Api.Features.PhotoImports;

// The database job is authoritative and the queue message is only a wake-up signal, so a
// failed enqueue is logged and reported to the caller rather than thrown: the job stays
// pending and the worker dispatches it again.
internal sealed class PhotoJobDispatcher(
    PhotoStorage storage,
    TravelMemoryDbContext dbContext,
    ILogger<PhotoJobDispatcher> logger)
{
    public async Task<bool> TryDispatchAsync(
        PhotoProcessingJob job,
        CancellationToken cancellationToken)
    {
        if (await TryEnqueueAsync(job, cancellationToken))
        {
            return true;
        }

        await SaveFailedDispatchesAsync(cancellationToken);
        return false;
    }

    public async Task<bool> TryDispatchAllAsync(
        IEnumerable<PhotoProcessingJob> jobs,
        CancellationToken cancellationToken)
    {
        // Attempt every job even after a failure, so one unavailable message does not
        // hold back the rest of the batch.
        var success = true;
        foreach (var job in jobs)
        {
            success &= await TryEnqueueAsync(job, cancellationToken);
        }

        if (!success)
        {
            await SaveFailedDispatchesAsync(cancellationToken);
        }

        return success;
    }

    private async Task<bool> TryEnqueueAsync(
        PhotoProcessingJob job,
        CancellationToken cancellationToken)
    {
        try
        {
            await storage.EnqueueAsync(job, cancellationToken);
            return true;
        }
        catch (RequestFailedException exception)
        {
            logger.LogError(
                exception,
                "Could not enqueue photo processing job {JobId}. The database job remains pending.",
                job.Id);
            job.MarkDispatchFailed();
            return false;
        }
    }

    // Best effort: if this save fails, the worker still redispatches the job once the
    // lost-message timeout has passed.
    private async Task SaveFailedDispatchesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(
                exception,
                "Could not record failed dispatches; the worker redispatches them after the lost-message timeout.");
        }
    }
}
