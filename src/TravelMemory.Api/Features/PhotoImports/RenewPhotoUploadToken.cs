using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TravelMemory.Api.Auth;
using TravelMemory.Domain.Photos;
using TravelMemory.Persistence.Data;

namespace TravelMemory.Api.Features.PhotoImports;

internal static class RenewPhotoUploadToken
{
    public static async Task<Results<Ok<UploadGrantResponse>, NotFound, Conflict<string>>>
        HandleAsync(
            Guid batchId,
            Guid itemId,
            ICurrentUser currentUser,
            PhotoStorage storage,
            TravelMemoryDbContext dbContext,
            CancellationToken cancellationToken)
    {
        var item = await dbContext.PhotoImportItems
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value =>
                    value.Id == itemId
                    && value.ImportBatchId == batchId
                    && value.OwnerId == currentUser.OwnerId,
                cancellationToken);

        if (item is null)
        {
            return TypedResults.NotFound();
        }

        if (item.State != PhotoImportItemState.AwaitingUpload)
        {
            return TypedResults.Conflict("The file is no longer awaiting upload.");
        }

        return TypedResults.Ok(storage.CreateUploadGrant(item.TemporaryBlobName));
    }
}
