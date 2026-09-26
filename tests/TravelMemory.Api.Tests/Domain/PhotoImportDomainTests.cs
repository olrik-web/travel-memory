using TravelMemory.Domain.Photos;

namespace TravelMemory.Api.Tests.Domain;

public sealed class PhotoImportDomainTests
{
    private static readonly Guid OwnerId =
        Guid.Parse("3eb4e33e-a764-487d-a70d-c02a89bc9ec4");
    private static readonly Guid TripId =
        Guid.Parse("0d5357e7-5c63-4450-aab2-841549cb735b");
    private static readonly DateTimeOffset Now =
        new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Batch_enforces_the_500_file_limit()
    {
        var batch = PhotoImportBatch.Create(
            OwnerId,
            TripId,
            Guid.NewGuid(),
            PhotoImportBatch.MaximumFileCount,
            Now);

        Assert.Equal(PhotoImportBatch.MaximumFileCount, batch.ExpectedFileCount);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PhotoImportBatch.Create(
                OwnerId,
                TripId,
                Guid.NewGuid(),
                PhotoImportBatch.MaximumFileCount + 1,
                Now));
    }

    [Fact]
    public void Photo_preserves_original_time_and_stores_the_adjusted_local_timeline()
    {
        var batch = PhotoImportBatch.Create(OwnerId, TripId, Guid.NewGuid(), 1, Now);
        var item = CreateItem(batch);
        var original = new DateTime(2026, 8, 17, 14, 23, 9, DateTimeKind.Unspecified);
        item.MarkReadyForReview(new string('a', 64), original, 120, Now);

        var photo = Photo.Create(
            item,
            -60,
            "photo/web.jpg",
            "photo/thumb.jpg",
            1600,
            1200,
            480,
            360,
            Now);

        Assert.Equal(original, photo.CapturedAtOriginalLocal);
        Assert.Equal(-60, photo.TimeAdjustmentMinutes);
        Assert.Equal(original.AddMinutes(-60), photo.CapturedAtTimelineLocal);
        Assert.Equal(DateTimeKind.Unspecified, photo.CapturedAtTimelineLocal.Kind);
        Assert.Equal(120, photo.ExifOffsetMinutes);
    }

    [Fact]
    public void Job_retry_and_abandoned_recovery_are_explicit_and_bounded()
    {
        var item = CreateItem(
            PhotoImportBatch.Create(OwnerId, TripId, Guid.NewGuid(), 1, Now));
        var job = PhotoProcessingJob.Create(item, PhotoProcessingJobKind.Analyze, Now, traceParent: null);

        Assert.True(job.TryStart(Now));
        Assert.Equal(1, job.AttemptCount);
        job.Retry("temporary", Now, TimeSpan.FromSeconds(10));
        Assert.False(job.TryStart(Now.AddSeconds(9)));
        Assert.True(job.TryStart(Now.AddSeconds(10)));
        Assert.Equal(2, job.AttemptCount);
        Assert.True(job.RecoverIfAbandoned(Now.AddMinutes(20), TimeSpan.FromMinutes(10)));
        Assert.Equal(PhotoProcessingJobState.Pending, job.State);
    }

    private static PhotoImportItem CreateItem(PhotoImportBatch batch) =>
        PhotoImportItem.Create(
            OwnerId,
            TripId,
            batch.Id,
            new string('b', 64),
            "photo.jpg",
            "image/jpeg",
            1024,
            "imports/photo.jpg",
            Now);
}
