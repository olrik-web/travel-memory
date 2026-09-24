namespace TravelMemory.Domain.Photos;

public static class PhotoImportBatchStateCalculator
{
    public static PhotoImportBatchState Calculate(
        PhotoImportBatch batch,
        IReadOnlyCollection<PhotoImportItem> items)
    {
        if (items.Any(item => item.State == PhotoImportItemState.AwaitingUpload))
        {
            return PhotoImportBatchState.Uploading;
        }

        if (items.Any(item =>
                item.State is PhotoImportItemState.QueuedForAnalysis
                    or PhotoImportItemState.Analyzing))
        {
            return PhotoImportBatchState.Analyzing;
        }

        var readyItems = items.Count(item => item.State == PhotoImportItemState.ReadyForReview);
        if (readyItems > 0 && batch.TimeAdjustmentMinutes is null)
        {
            return PhotoImportBatchState.ReadyForReview;
        }

        if (items.Any(item =>
                item.State is PhotoImportItemState.QueuedForProcessing
                    or PhotoImportItemState.Processing
                    or PhotoImportItemState.CleanupPending))
        {
            return PhotoImportBatchState.Processing;
        }

        return items.Any(item =>
            item.State is PhotoImportItemState.Failed or PhotoImportItemState.CleanupFailed)
            ? PhotoImportBatchState.CompletedWithErrors
            : PhotoImportBatchState.Completed;
    }
}
