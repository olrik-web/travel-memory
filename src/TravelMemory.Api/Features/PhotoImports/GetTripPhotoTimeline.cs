using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TravelMemory.Api.Auth;
using TravelMemory.Persistence.Data;

namespace TravelMemory.Api.Features.PhotoImports;

internal static class GetTripPhotoTimeline
{
    public static async Task<Results<Ok<PhotoTimelineResponse>, NotFound>> HandleAsync(
        Guid tripId,
        ICurrentUser currentUser,
        PhotoStorage storage,
        TravelMemoryDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var tripExists = await dbContext.Trips.AnyAsync(
            trip => trip.Id == tripId && trip.OwnerId == currentUser.OwnerId,
            cancellationToken);
        if (!tripExists)
        {
            return TypedResults.NotFound();
        }

        var photos = await dbContext.Photos
            .AsNoTracking()
            .Where(photo => photo.TripId == tripId && photo.OwnerId == currentUser.OwnerId)
            .OrderBy(photo => photo.CapturedAtTimelineLocal)
            .ThenBy(photo => photo.Id)
            .ToListAsync(cancellationToken);
        var signer = await storage.GetSasSignerAsync(cancellationToken);
        var timeline = photos.Select(photo =>
        {
            var thumbnail = storage.CreateReadGrant(signer, photo.ThumbnailBlobName);
            var web = storage.CreateReadGrant(signer, photo.WebBlobName);
            return new PhotoTimelineItemResponse(
                photo.Id,
                photo.OriginalFileName,
                photo.CapturedAtOriginalLocal,
                photo.TimeAdjustmentMinutes,
                photo.CapturedAtTimelineLocal,
                photo.ExifOffsetMinutes,
                thumbnail.Url,
                web.Url,
                thumbnail.ExpiresAtUtc < web.ExpiresAtUtc
                    ? thumbnail.ExpiresAtUtc
                    : web.ExpiresAtUtc,
                photo.Width,
                photo.Height,
                photo.ThumbnailWidth,
                photo.ThumbnailHeight);
        }).ToList();

        return TypedResults.Ok(new PhotoTimelineResponse(timeline));
    }
}
