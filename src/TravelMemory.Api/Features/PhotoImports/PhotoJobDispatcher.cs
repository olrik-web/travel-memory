using Azure;
using TravelMemory.Domain.Photos;

namespace TravelMemory.Api.Features.PhotoImports;

// The database job is authoritative and the queue message is only a wake-up signal, so a
// failed enqueue is logged and reported to the caller rather than thrown: the job stays
// pending and can be dispatched again.
internal sealed class PhotoJobDispatcher(
    PhotoStorage storage,
    ILogger<PhotoJobDispatcher> logger)
{
    public async Task<bool> TryDispatchAsync(
        PhotoProcessingJob job,
        CancellationToken cancellationToken)
    {
        try
        {
            await storage.EnqueueAsync(job.Id, cancellationToken);
            return true;
        }
        catch (RequestFailedException exception)
        {
            logger.LogError(
                exception,
                "Could not enqueue photo processing job {JobId}. The database job remains pending.",
                job.Id);
            return false;
        }
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
            success &= await TryDispatchAsync(job, cancellationToken);
        }

        return success;
    }
}
