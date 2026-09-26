namespace TravelMemory.Domain.Photos;

public sealed class PhotoProcessingJob
{
    public const int MaximumAttempts = 5;
    public const int MaxTraceParentLength = 55;

    // A job still processing after this long has lost its worker: the maintenance cycle
    // recovers it, and deleting its trip no longer waits for it.
    public static readonly TimeSpan AbandonedAfter = TimeSpan.FromMinutes(10);

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

    // The W3C trace context of the operation that created or last reset the job. Every
    // message for the job carries it, so retries, redispatches, and follow-up jobs continue
    // that operation's trace instead of starting unrelated ones.
    public string? TraceParent { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public byte[] Version { get; private set; } = [];

    public static PhotoProcessingJob Create(
        PhotoImportItem item,
        PhotoProcessingJobKind kind,
        DateTimeOffset createdAt,
        string? traceParent)
    {
        var job = new PhotoProcessingJob(
            Guid.NewGuid(),
            item.OwnerId,
            item.ImportBatchId,
            item.Id,
            kind,
            createdAt);
        job.TraceParent = ValidTraceParent(traceParent);
        return job;
    }

    // A scheduled job runs long after the operation that scheduled it, so it starts its own
    // trace rather than stretching that operation's trace over days.
    public static PhotoProcessingJob Schedule(
        PhotoImportItem item,
        PhotoProcessingJobKind kind,
        DateTimeOffset createdAt,
        DateTimeOffset availableAt)
    {
        var job = Create(item, kind, createdAt, traceParent: null);
        job.AvailableAtUtc = availableAt.ToUniversalTime();
        return job;
    }

    // Recorded before the queue message is sent, so the worker only redispatches jobs whose
    // message was never sent for the current availability or appears to be lost.
    public void MarkDispatched(DateTimeOffset dispatchedAt)
    {
        LastDispatchedAtUtc = dispatchedAt.ToUniversalTime();
    }

    // No message was sent, so no worker can be processing the job, and the worker's next
    // redispatch cycle sends it instead of waiting for the lost-message timeout.
    public void MarkDispatchFailed()
    {
        LastDispatchedAtUtc = null;
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

    public void ResetForManualRetry(DateTimeOffset resetAt, string? traceParent)
    {
        State = PhotoProcessingJobState.Pending;
        AttemptCount = 0;
        AvailableAtUtc = resetAt.ToUniversalTime();
        LastError = null;
        UpdatedAtUtc = resetAt.ToUniversalTime();
        TraceParent = ValidTraceParent(traceParent);
    }

    public bool IsAbandoned(DateTimeOffset now, TimeSpan timeout) =>
        State == PhotoProcessingJobState.Processing
        && UpdatedAtUtc <= now.ToUniversalTime().Subtract(timeout);

    public bool RecoverIfAbandoned(DateTimeOffset recoveredAt, TimeSpan timeout)
    {
        if (!IsAbandoned(recoveredAt, timeout))
        {
            return false;
        }

        State = PhotoProcessingJobState.Pending;
        AvailableAtUtc = recoveredAt.ToUniversalTime();
        LastError = "Recovered after the previous worker stopped before completing the job.";
        UpdatedAtUtc = recoveredAt.ToUniversalTime();
        return true;
    }

    // Tracing is diagnostic only, so an unexpected format is dropped rather than rejected.
    private static string? ValidTraceParent(string? traceParent) =>
        traceParent?.Length <= MaxTraceParentLength ? traceParent : null;
}
