using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TravelMemory.Api.Auth;
using TravelMemory.Persistence.Data;

namespace TravelMemory.Api.Features.PhotoImports;

internal static class DeletePhoto
{
    // The derivatives go before the row. A failure in between leaves a photo whose images
    // are gone, which the next attempt removes; the other order could leave blobs that no
    // row points to anymore. Removing the row also frees the photo's content hash, so the
    // same photo can be imported again.
    public static async Task<Results<NoContent, NotFound>> HandleAsync(
        Guid tripId,
        Guid photoId,
        ICurrentUser currentUser,
        PhotoStorage storage,
        TravelMemoryDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var photo = await dbContext.Photos.SingleOrDefaultAsync(
            value =>
                value.Id == photoId
                && value.TripId == tripId
                && value.OwnerId == currentUser.OwnerId,
            cancellationToken);
        if (photo is null)
        {
            return TypedResults.NotFound();
        }

        await storage.DeleteDerivativesAsync(
            photo.WebBlobName,
            photo.ThumbnailBlobName,
            cancellationToken);
        dbContext.Photos.Remove(photo);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent request deleted it first.
            return TypedResults.NotFound();
        }

        return TypedResults.NoContent();
    }
}
