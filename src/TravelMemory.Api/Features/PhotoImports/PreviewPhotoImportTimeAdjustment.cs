using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TravelMemory.Api.Auth;
using TravelMemory.Domain.Photos;
using TravelMemory.Persistence.Data;

namespace TravelMemory.Api.Features.PhotoImports;

internal static class PreviewPhotoImportTimeAdjustment
{
    public static async Task<Results<Ok<PhotoTimePreviewResponse>, ValidationProblem, NotFound>>
        HandleAsync(
            Guid batchId,
            int adjustmentMinutes,
            ICurrentUser currentUser,
            TravelMemoryDbContext dbContext,
            CancellationToken cancellationToken)
    {
        if (adjustmentMinutes is
            < -PhotoImportBatch.MaximumAdjustmentMinutes
            or > PhotoImportBatch.MaximumAdjustmentMinutes)
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["adjustmentMinutes"] = ["The time adjustment must be between -24 and +24 hours."],
                });
        }

        var batchExists = await dbContext.PhotoImportBatches.AnyAsync(
            batch => batch.Id == batchId && batch.OwnerId == currentUser.OwnerId,
            cancellationToken);
        if (!batchExists)
        {
            return TypedResults.NotFound();
        }

        var items = await dbContext.PhotoImportItems
            .AsNoTracking()
            .Where(item =>
                item.ImportBatchId == batchId
                && item.OwnerId == currentUser.OwnerId
                && item.CapturedAtOriginalLocal != null)
            .OrderBy(item => item.CapturedAtOriginalLocal)
            .Select(item => new PhotoTimePreviewItemResponse(
                item.Id,
                item.OriginalFileName,
                item.CapturedAtOriginalLocal!.Value,
                item.CapturedAtOriginalLocal.Value.AddMinutes(adjustmentMinutes)))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new PhotoTimePreviewResponse(adjustmentMinutes, items));
    }
}
