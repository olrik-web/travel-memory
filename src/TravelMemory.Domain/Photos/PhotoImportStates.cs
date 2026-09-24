namespace TravelMemory.Domain.Photos;

public enum PhotoImportBatchState
{
    Uploading,
    Analyzing,
    ReadyForReview,
    Processing,
    Completed,
    CompletedWithErrors,
}

public enum PhotoImportItemState
{
    AwaitingUpload,
    QueuedForAnalysis,
    Analyzing,
    ReadyForReview,
    QueuedForProcessing,
    Processing,
    CleanupPending,
    Succeeded,
    Failed,
    CleanupFailed,
}

public enum PhotoImportItemOutcome
{
    None,
    Imported,
    Duplicate,
}

public enum PhotoProcessingJobKind
{
    Analyze,
    Process,
    Cleanup,
    ExpireFailedOriginal,
}

public enum PhotoProcessingJobState
{
    Pending,
    Processing,
    Succeeded,
    Failed,
}
