namespace TravelMemory.Domain.Photos;

public sealed class PhotoImportItem
{
    private PhotoImportItem()
    {
    }

    private PhotoImportItem(
        Guid id,
        Guid ownerId,
        Guid tripId,
        Guid importBatchId,
        string clientFileId,
        string originalFileName,
        string contentType,
        long expectedSizeBytes,
        string temporaryBlobName,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OwnerId = ownerId;
        TripId = tripId;
        ImportBatchId = importBatchId;
        ClientFileId = clientFileId;
        OriginalFileName = originalFileName;
        ContentType = contentType;
        ExpectedSizeBytes = expectedSizeBytes;
        TemporaryBlobName = temporaryBlobName;
        State = PhotoImportItemState.AwaitingUpload;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public Guid TripId { get; private set; }

    public Guid ImportBatchId { get; private set; }

    public string ClientFileId { get; private set; } = string.Empty;

    public string OriginalFileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long ExpectedSizeBytes { get; private set; }

    public string TemporaryBlobName { get; private set; } = string.Empty;

    public PhotoImportItemState State { get; private set; }

    public PhotoImportItemOutcome Outcome { get; private set; }

    public string? ContentHash { get; private set; }

    public DateTime? CapturedAtOriginalLocal { get; private set; }

    public int? ExifOffsetMinutes { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTimeOffset? DerivativesVerifiedAtUtc { get; private set; }

    public DateTimeOffset? OriginalDeletedAtUtc { get; private set; }

    public DateTimeOffset? OriginalRetainedUntilUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public byte[] Version { get; private set; } = [];

    public static PhotoImportItem Create(
        Guid ownerId,
        Guid tripId,
        Guid importBatchId,
        string clientFileId,
        string originalFileName,
        string contentType,
        long expectedSizeBytes,
        string temporaryBlobName,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientFileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryBlobName);

        if (expectedSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedSizeBytes));
        }

        return new PhotoImportItem(
            Guid.NewGuid(),
            ownerId,
            tripId,
            importBatchId,
            clientFileId,
            originalFileName,
            contentType,
            expectedSizeBytes,
            temporaryBlobName,
            createdAt);
    }

    public void QueueForAnalysis(DateTimeOffset updatedAt)
    {
        ClearError();
        UpdateState(PhotoImportItemState.QueuedForAnalysis, updatedAt);
    }

    public void MarkAnalyzing(DateTimeOffset updatedAt) =>
        UpdateState(PhotoImportItemState.Analyzing, updatedAt);

    public void MarkReadyForReview(
        string contentHash,
        DateTime capturedAtOriginalLocal,
        int? exifOffsetMinutes,
        DateTimeOffset updatedAt)
    {
        ContentHash = contentHash;
        CapturedAtOriginalLocal = DateTime.SpecifyKind(
            capturedAtOriginalLocal,
            DateTimeKind.Unspecified);
        ExifOffsetMinutes = exifOffsetMinutes;
        ClearError();
        UpdateState(PhotoImportItemState.ReadyForReview, updatedAt);
    }

    public void QueueForProcessing(DateTimeOffset updatedAt)
    {
        ClearError();
        UpdateState(PhotoImportItemState.QueuedForProcessing, updatedAt);
    }

    public void MarkProcessing(DateTimeOffset updatedAt) =>
        UpdateState(PhotoImportItemState.Processing, updatedAt);

    public void MarkCleanupPending(
        PhotoImportItemOutcome outcome,
        DateTimeOffset derivativesVerifiedAt,
        DateTimeOffset updatedAt)
    {
        Outcome = outcome;
        DerivativesVerifiedAtUtc = derivativesVerifiedAt.ToUniversalTime();
        ClearError();
        UpdateState(PhotoImportItemState.CleanupPending, updatedAt);
    }

    public void MarkSucceeded(DateTimeOffset originalDeletedAt)
    {
        OriginalDeletedAtUtc = originalDeletedAt.ToUniversalTime();
        ClearError();
        UpdateState(PhotoImportItemState.Succeeded, originalDeletedAt);
    }

    public void MarkFailed(string code, string message, DateTimeOffset updatedAt)
    {
        ErrorCode = code;
        ErrorMessage = message;
        UpdateState(PhotoImportItemState.Failed, updatedAt);
    }

    public void RetainFailedOriginalUntil(DateTimeOffset deleteAfter)
    {
        OriginalRetainedUntilUtc = deleteAfter.ToUniversalTime();
    }

    public void MarkFailedOriginalDeleted(DateTimeOffset originalDeletedAt)
    {
        OriginalDeletedAtUtc = originalDeletedAt.ToUniversalTime();
        OriginalRetainedUntilUtc = null;
        UpdatedAtUtc = originalDeletedAt.ToUniversalTime();
    }

    public void ClearFailedOriginalRetention()
    {
        OriginalRetainedUntilUtc = null;
    }

    public void MarkCleanupFailed(string code, string message, DateTimeOffset updatedAt)
    {
        ErrorCode = code;
        ErrorMessage = message;
        UpdateState(PhotoImportItemState.CleanupFailed, updatedAt);
    }

    private void UpdateState(PhotoImportItemState state, DateTimeOffset updatedAt)
    {
        State = state;
        UpdatedAtUtc = updatedAt.ToUniversalTime();
    }

    private void ClearError()
    {
        ErrorCode = null;
        ErrorMessage = null;
    }
}
