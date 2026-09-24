namespace TravelMemory.Persistence.Photos;

public static class PhotoStorageNames
{
    public const string TemporaryContainer = "photo-imports";
    public const string PermanentContainer = "photos";
    public const string ProcessingQueue = "photo-processing";
    public const long MaximumFileSizeBytes = 100L * 1024 * 1024;
}

public sealed record PhotoQueueMessage(Guid JobId);
