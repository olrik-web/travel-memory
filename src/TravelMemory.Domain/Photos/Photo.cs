namespace TravelMemory.Domain.Photos;

public sealed class Photo
{
    private Photo()
    {
    }

    private Photo(
        Guid id,
        Guid ownerId,
        Guid tripId,
        Guid importBatchId,
        Guid importItemId,
        string contentHash,
        string originalFileName,
        DateTime capturedAtOriginalLocal,
        int timeAdjustmentMinutes,
        DateTime capturedAtTimelineLocal,
        int? exifOffsetMinutes,
        string webBlobName,
        string thumbnailBlobName,
        int width,
        int height,
        int thumbnailWidth,
        int thumbnailHeight,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OwnerId = ownerId;
        TripId = tripId;
        ImportBatchId = importBatchId;
        ImportItemId = importItemId;
        ContentHash = contentHash;
        OriginalFileName = originalFileName;
        CapturedAtOriginalLocal = DateTime.SpecifyKind(
            capturedAtOriginalLocal,
            DateTimeKind.Unspecified);
        TimeAdjustmentMinutes = timeAdjustmentMinutes;
        CapturedAtTimelineLocal = DateTime.SpecifyKind(
            capturedAtTimelineLocal,
            DateTimeKind.Unspecified);
        ExifOffsetMinutes = exifOffsetMinutes;
        WebBlobName = webBlobName;
        ThumbnailBlobName = thumbnailBlobName;
        Width = width;
        Height = height;
        ThumbnailWidth = thumbnailWidth;
        ThumbnailHeight = thumbnailHeight;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public Guid TripId { get; private set; }

    public Guid ImportBatchId { get; private set; }

    public Guid ImportItemId { get; private set; }

    public string ContentHash { get; private set; } = string.Empty;

    public string OriginalFileName { get; private set; } = string.Empty;

    public DateTime CapturedAtOriginalLocal { get; private set; }

    public int TimeAdjustmentMinutes { get; private set; }

    public DateTime CapturedAtTimelineLocal { get; private set; }

    public int? ExifOffsetMinutes { get; private set; }

    public string WebBlobName { get; private set; } = string.Empty;

    public string ThumbnailBlobName { get; private set; } = string.Empty;

    public int Width { get; private set; }

    public int Height { get; private set; }

    public int ThumbnailWidth { get; private set; }

    public int ThumbnailHeight { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Photo Create(
        PhotoImportItem item,
        int timeAdjustmentMinutes,
        string webBlobName,
        string thumbnailBlobName,
        int width,
        int height,
        int thumbnailWidth,
        int thumbnailHeight,
        DateTimeOffset createdAt)
    {
        if (item.ContentHash is null || item.CapturedAtOriginalLocal is null)
        {
            throw new InvalidOperationException("The import item has not been analyzed.");
        }

        return new Photo(
            item.Id,
            item.OwnerId,
            item.TripId,
            item.ImportBatchId,
            item.Id,
            item.ContentHash,
            item.OriginalFileName,
            item.CapturedAtOriginalLocal.Value,
            timeAdjustmentMinutes,
            item.CapturedAtOriginalLocal.Value.AddMinutes(timeAdjustmentMinutes),
            item.ExifOffsetMinutes,
            webBlobName,
            thumbnailBlobName,
            width,
            height,
            thumbnailWidth,
            thumbnailHeight,
            createdAt);
    }
}
