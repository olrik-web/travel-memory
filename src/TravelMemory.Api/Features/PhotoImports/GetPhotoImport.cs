using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TravelMemory.Api.Auth;
using TravelMemory.Persistence.Data;

namespace TravelMemory.Api.Features.PhotoImports;

internal static class GetPhotoImport
{
    public static async Task<Results<Ok<PhotoImportBatchResponse>, NotFound>> HandleAsync(
        Guid batchId,
        ICurrentUser currentUser,
        PhotoStorage storage,
        TravelMemoryDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var batch = await dbContext.PhotoImportBatches
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.Id == batchId && value.OwnerId == currentUser.OwnerId,
                cancellationToken);
        if (batch is null)
        {
            return TypedResults.NotFound();
        }

        var items = await dbContext.PhotoImportItems
            .AsNoTracking()
            .Where(item => item.ImportBatchId == batch.Id)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(
            PhotoImportResponseFactory.Create(
                batch,
                items,
                storage,
                includeUploadGrants: false));
    }
}
