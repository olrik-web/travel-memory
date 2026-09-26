namespace TravelMemory.Persistence.Photos;

public static class PhotoStorageNames
{
    public const string TemporaryContainer = "photo-imports";
    public const string PermanentContainer = "photos";
    public const string ProcessingQueue = "photo-processing";
    public const long MaximumFileSizeBytes = 100L * 1024 * 1024;

    // Every blob of a trip, original or derivative, is named under this prefix in its
    // container, so deleting a trip can find all of them without the database.
    public static string TripPrefix(Guid ownerId, Guid tripId) =>
        $"owners/{ownerId:N}/trips/{tripId:N}/";
}

// TraceParent is optional so that messages sent before it existed still deserialize.
public sealed record PhotoQueueMessage(Guid JobId, string? TraceParent = null);
