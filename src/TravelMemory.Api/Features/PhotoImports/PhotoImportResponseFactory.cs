using TravelMemory.Domain.Photos;

namespace TravelMemory.Api.Features.PhotoImports;

internal static class PhotoImportResponseFactory
{
    public static PhotoImportBatchResponse Create(
        PhotoImportBatch batch,
        IReadOnlyCollection<PhotoImportItem> items,
        PhotoStorage storage,
        bool includeUploadGrants)
    {
        var responses = items
            .OrderBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.Id)
            .Select(item =>
            {
                UploadGrantResponse? grant = null;
                if (includeUploadGrants && item.State == PhotoImportItemState.AwaitingUpload)
                {
                    grant = storage.CreateUploadGrant(item.TemporaryBlobName);
                }

                return new PhotoImportItemResponse(
                    item.Id,
                    item.ClientFileId,
                    item.OriginalFileName,
                    item.ContentType,
                    item.ExpectedSizeBytes,
                    item.State.ToString(),
                    item.Outcome.ToString(),
                    item.CapturedAtOriginalLocal,
                    item.ExifOffsetMinutes,
                    batch.TimeAdjustmentMinutes is not null
                        && item.CapturedAtOriginalLocal is not null
                            ? item.CapturedAtOriginalLocal.Value.AddMinutes(
                                batch.TimeAdjustmentMinutes.Value)
                            : null,
                    item.ErrorCode,
                    item.ErrorMessage,
                    item.OriginalRetainedUntilUtc,
                    item.OriginalDeletedAtUtc,
                    CanRetry(item),
                    grant?.UploadUrl,
                    grant?.ExpiresAtUtc);
            })
            .ToList();

        return new PhotoImportBatchResponse(
            batch.Id,
            batch.TripId,
            batch.State.ToString(),
            batch.TimeAdjustmentMinutes,
            CreateCounts(items),
            responses);
    }

    private static PhotoImportCountsResponse CreateCounts(
        IReadOnlyCollection<PhotoImportItem> items) =>
        new(
            items.Count,
            items.Count(item => item.State == PhotoImportItemState.AwaitingUpload),
            items.Count(item =>
                item.State is PhotoImportItemState.QueuedForAnalysis
                    or PhotoImportItemState.Analyzing),
            items.Count(item => item.State == PhotoImportItemState.ReadyForReview),
            items.Count(item =>
                item.State is PhotoImportItemState.QueuedForProcessing
                    or PhotoImportItemState.Processing
                    or PhotoImportItemState.CleanupPending),
            items.Count(item =>
                item.State == PhotoImportItemState.Succeeded
                && item.Outcome == PhotoImportItemOutcome.Imported),
            items.Count(item =>
                item.State == PhotoImportItemState.Succeeded
                && item.Outcome == PhotoImportItemOutcome.Duplicate),
            items.Count(item =>
                item.State is PhotoImportItemState.Failed
                    or PhotoImportItemState.CleanupFailed));

    public static bool CanRetry(PhotoImportItem item) =>
        item.State == PhotoImportItemState.CleanupFailed
        || item.State == PhotoImportItemState.Failed
        && item.ErrorCode is "temporary_failure" or "processing_exhausted";
}
