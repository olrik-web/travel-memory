namespace TravelMemory.Api.Features.PhotoImports;

public sealed record CreatePhotoImportRequest(
    Guid ClientBatchId,
    IReadOnlyList<CreatePhotoImportFileRequest>? Files);

public sealed record CreatePhotoImportFileRequest(
    string? ClientFileId,
    string? FileName,
    string? ContentType,
    long SizeBytes);

public sealed record FinalizePhotoImportRequest(int TimeAdjustmentMinutes);

public sealed record PhotoImportBatchResponse(
    Guid Id,
    Guid TripId,
    string State,
    int? TimeAdjustmentMinutes,
    PhotoImportCountsResponse Counts,
    IReadOnlyList<PhotoImportItemResponse> Items);

public sealed record PhotoImportCountsResponse(
    int Total,
    int AwaitingUpload,
    int Analyzing,
    int Ready,
    int Processing,
    int Succeeded,
    int Duplicates,
    int Failed);

public sealed record PhotoImportItemResponse(
    Guid Id,
    string ClientFileId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string State,
    string Outcome,
    DateTime? CapturedAtOriginalLocal,
    int? ExifOffsetMinutes,
    DateTime? CapturedAtTimelineLocal,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? OriginalRetainedUntilUtc,
    DateTimeOffset? OriginalDeletedAtUtc,
    bool CanRetry,
    Uri? UploadUrl,
    DateTimeOffset? UploadExpiresAtUtc);

public sealed record UploadGrantResponse(Uri UploadUrl, DateTimeOffset ExpiresAtUtc);

public sealed record PhotoTimePreviewResponse(
    int TimeAdjustmentMinutes,
    IReadOnlyList<PhotoTimePreviewItemResponse> Items);

public sealed record PhotoTimePreviewItemResponse(
    Guid ItemId,
    string FileName,
    DateTime CapturedAtOriginalLocal,
    DateTime CapturedAtTimelineLocal);

public sealed record PhotoTimelineResponse(IReadOnlyList<PhotoTimelineItemResponse> Items);

public sealed record PhotoTimelineItemResponse(
    Guid Id,
    string FileName,
    DateTime CapturedAtOriginalLocal,
    int TimeAdjustmentMinutes,
    DateTime CapturedAtTimelineLocal,
    int? ExifOffsetMinutes,
    Uri ThumbnailUrl,
    Uri WebUrl,
    DateTimeOffset UrlsExpireAtUtc,
    int Width,
    int Height,
    int ThumbnailWidth,
    int ThumbnailHeight);
