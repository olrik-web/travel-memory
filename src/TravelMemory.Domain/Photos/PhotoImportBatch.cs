namespace TravelMemory.Domain.Photos;

public sealed class PhotoImportBatch
{
    public const int MaximumFileCount = 500;
    public const int MaximumAdjustmentMinutes = 24 * 60;

    private PhotoImportBatch()
    {
    }

    private PhotoImportBatch(
        Guid id,
        Guid ownerId,
        Guid tripId,
        Guid clientBatchId,
        int expectedFileCount,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OwnerId = ownerId;
        TripId = tripId;
        ClientBatchId = clientBatchId;
        ExpectedFileCount = expectedFileCount;
        State = PhotoImportBatchState.Uploading;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public Guid TripId { get; private set; }

    public Guid ClientBatchId { get; private set; }

    public int ExpectedFileCount { get; private set; }

    public PhotoImportBatchState State { get; private set; }

    public int? TimeAdjustmentMinutes { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static PhotoImportBatch Create(
        Guid ownerId,
        Guid tripId,
        Guid clientBatchId,
        int expectedFileCount,
        DateTimeOffset createdAt)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("OwnerId cannot be empty.", nameof(ownerId));
        }

        if (tripId == Guid.Empty)
        {
            throw new ArgumentException("TripId cannot be empty.", nameof(tripId));
        }

        if (clientBatchId == Guid.Empty)
        {
            throw new ArgumentException("ClientBatchId cannot be empty.", nameof(clientBatchId));
        }

        if (expectedFileCount is < 1 or > MaximumFileCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedFileCount),
                $"A batch must contain between 1 and {MaximumFileCount} files.");
        }

        return new PhotoImportBatch(
            Guid.NewGuid(),
            ownerId,
            tripId,
            clientBatchId,
            expectedFileCount,
            createdAt);
    }

    public void SetTimeAdjustment(int minutes, DateTimeOffset updatedAt)
    {
        if (minutes is < -MaximumAdjustmentMinutes or > MaximumAdjustmentMinutes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minutes),
                $"Time adjustment must be within +/- {MaximumAdjustmentMinutes} minutes.");
        }

        TimeAdjustmentMinutes = minutes;
        UpdatedAtUtc = updatedAt.ToUniversalTime();
    }

    public void SetState(PhotoImportBatchState state, DateTimeOffset updatedAt)
    {
        State = state;
        UpdatedAtUtc = updatedAt.ToUniversalTime();
    }
}
