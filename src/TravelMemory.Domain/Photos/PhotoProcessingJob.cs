namespace TravelMemory.Domain.Photos;

public sealed class PhotoProcessingJob
{
    public const int MaximumAttempts = 5;

    private PhotoProcessingJob()
    {
    }

    private PhotoProcessingJob(
        Guid id,
        Guid ownerId,
        Guid importBatchId,
        Guid importItemId,
        PhotoProcessingJobKind kind,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OwnerId = ownerId;
        ImportBatchId = importBatchId;
        ImportItemId = importItemId;
        Kind = kind;
        State = PhotoProcessingJobState.Pending;
        AvailableAtUtc = createdAtUtc.ToUniversalTime();
        CreatedAtUtc = AvailableAtUtc;
        UpdatedAtUtc = AvailableAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public Guid ImportBatchId { get; private set; }

    public Guid ImportItemId { get; private set; }

    public PhotoProcessingJobKind Kind { get; private set; }

    public PhotoProcessingJobState State { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTimeOffset AvailableAtUtc { get; private set; }

    public DateTimeOffset? LastDispatchedAtUtc { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public byte[] Version { get; private set; } = [];

    public static PhotoProcessingJob Create(
        PhotoImportItem item,
        PhotoProcessingJobKind kind,
        DateTimeOffset createdAt) =>
        new(
            Guid.NewGuid(),
            item.OwnerId,
            item.ImportBatchId,
            item.Id,
            kind,
            createdAt);

    public static PhotoProcessingJob Schedule(
        PhotoImportItem item,
        PhotoProcessingJobKind kind,
        DateTimeOffset createdAt,
        DateTimeOffset availableAt)
    {
        var job = Create(item, kind, createdAt);
        job.AvailableAtUtc = availableAt.ToUniversalTime();
        return job;
    }

    // Recorded before the queue message is sent, so the worker only redispatches jobs whose
    // message was never sent for the current availability or appears to be lost.
    public void MarkDispatched(DateTimeOffset dispatchedAt)
    {
        LastDispatchedAtUtc = dispatchedAt.ToUniversalTime();
    }

    public bool TryStart(DateTimeOffset startedAt)
    {
        if (State != PhotoProcessingJobState.Pending || AvailableAtUtc > startedAt)
        {
            return false;
        }

        State = PhotoProcessingJobState.Processing;
        AttemptCount++;
        LastError = null;
        UpdatedAtUtc = startedAt.ToUniversalTime();
        return true;
    }

    public void Succeed(DateTimeOffset completedAt)
    {
        State = PhotoProcessingJobState.Succeeded;
        LastError = null;
        UpdatedAtUtc = completedAt.ToUniversalTime();
    }

    public void Retry(string error, DateTimeOffset failedAt, TimeSpan delay)
    {
        State = PhotoProcessingJobState.Pending;
        LastError = error;
        AvailableAtUtc = failedAt.ToUniversalTime().Add(delay);
        UpdatedAtUtc = failedAt.ToUniversalTime();
    }

    public void Fail(string error, DateTimeOffset failedAt)
    {
        State = PhotoProcessingJobState.Failed;
        LastError = error;
        UpdatedAtUtc = failedAt.ToUniversalTime();
    }

    public void ResetForManualRetry(DateTimeOffset resetAt)
    {
        State = PhotoProcessingJobState.Pending;
        AttemptCount = 0;
        AvailableAtUtc = resetAt.ToUniversalTime();
        LastError = null;
        UpdatedAtUtc = resetAt.ToUniversalTime();
    }

    public bool RecoverIfAbandoned(DateTimeOffset recoveredAt, TimeSpan timeout)
    {
        if (State != PhotoProcessingJobState.Processing
            || UpdatedAtUtc > recoveredAt.ToUniversalTime().Subtract(timeout))
        {
            return false;
        }

        State = PhotoProcessingJobState.Pending;
        AvailableAtUtc = recoveredAt.ToUniversalTime();
        LastError = "Recovered after the previous worker stopped before completing the job.";
        UpdatedAtUtc = recoveredAt.ToUniversalTime();
        return true;
    }
}
