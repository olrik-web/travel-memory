using System.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TravelMemory.Api.Auth;
using TravelMemory.Domain.Photos;
using TravelMemory.Persistence.Data;
using TravelMemory.Persistence.Photos;

namespace TravelMemory.Api.Features.PhotoImports;

internal static class CreatePhotoImport
{
    public static async Task<Results<
            Created<PhotoImportBatchResponse>,
            Ok<PhotoImportBatchResponse>,
            ValidationProblem,
            NotFound,
            Conflict<string>>>
        HandleAsync(
            Guid tripId,
            CreatePhotoImportRequest request,
            ICurrentUser currentUser,
            TimeProvider timeProvider,
            PhotoStorage storage,
            TravelMemoryDbContext dbContext,
            CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var tripExists = await dbContext.Trips.AnyAsync(
            trip => trip.Id == tripId && trip.OwnerId == currentUser.OwnerId,
            cancellationToken);
        if (!tripExists)
        {
            return TypedResults.NotFound();
        }

        var executionStrategy = dbContext.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(CreateWithinTransactionAsync);

        async Task<Results<
                Created<PhotoImportBatchResponse>,
                Ok<PhotoImportBatchResponse>,
                ValidationProblem,
                NotFound,
                Conflict<string>>>
            CreateWithinTransactionAsync()
        {
            // Serializable isolation makes the ClientBatchId lookup and insert atomic, so
            // concurrent retries of the same request resume one batch instead of creating two.
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            var existingBatch = await dbContext.PhotoImportBatches.SingleOrDefaultAsync(
                batch =>
                    batch.OwnerId == currentUser.OwnerId
                    && batch.TripId == tripId
                    && batch.ClientBatchId == request.ClientBatchId,
                cancellationToken);

            if (existingBatch is not null)
            {
                var existingItems = await dbContext.PhotoImportItems
                    .Where(item => item.ImportBatchId == existingBatch.Id)
                    .ToListAsync(cancellationToken);
                var requestedIds = request.Files!
                    .Select(file => file.ClientFileId!)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                var existingIds = existingItems
                    .Select(item => item.ClientFileId)
                    .Order(StringComparer.Ordinal)
                    .ToArray();

                if (!requestedIds.SequenceEqual(existingIds, StringComparer.Ordinal))
                {
                    return TypedResults.Conflict(
                        "ClientBatchId is already used for a different set of files.");
                }

                await transaction.CommitAsync(cancellationToken);
                return TypedResults.Ok(
                    PhotoImportResponseFactory.Create(
                        existingBatch,
                        existingItems,
                        storage,
                        uploadGrantSigner: await storage.GetSasSignerAsync(cancellationToken)));
            }

            var now = timeProvider.GetUtcNow();
            var batch = PhotoImportBatch.Create(
                currentUser.OwnerId,
                tripId,
                request.ClientBatchId,
                request.Files!.Count,
                now);
            var items = new List<PhotoImportItem>(request.Files.Count);

            foreach (var file in request.Files)
            {
                _ = SupportedPhotoMedia.TryNormalize(
                    file.FileName,
                    file.ContentType,
                    out var safeFileName,
                    out var contentType,
                    out var extension);
                var blobName =
                    $"{PhotoStorageNames.TripPrefix(currentUser.OwnerId, tripId)}imports/{batch.Id:N}/{Guid.NewGuid():N}{extension}";
                items.Add(
                    PhotoImportItem.Create(
                        currentUser.OwnerId,
                        tripId,
                        batch.Id,
                        file.ClientFileId!,
                        safeFileName,
                        contentType,
                        file.SizeBytes,
                        blobName,
                        now));
            }

            dbContext.PhotoImportBatches.Add(batch);
            dbContext.PhotoImportItems.AddRange(items);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return TypedResults.Created(
                $"/api/photo-imports/{batch.Id}",
                PhotoImportResponseFactory.Create(
                    batch,
                    items,
                    storage,
                    uploadGrantSigner: await storage.GetSasSignerAsync(cancellationToken)));
        }
    }

    private static Dictionary<string, string[]> Validate(CreatePhotoImportRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.ClientBatchId == Guid.Empty)
        {
            errors["clientBatchId"] = ["ClientBatchId must be a valid GUID."];
        }

        if (request.Files is null
            || request.Files.Count is < 1 or > PhotoImportBatch.MaximumFileCount)
        {
            errors["files"] =
            [
                $"Select between 1 and {PhotoImportBatch.MaximumFileCount} photos.",
            ];
            return errors;
        }

        if (request.Files
            .Select(file => file.ClientFileId)
            .Distinct(StringComparer.Ordinal)
            .Count() != request.Files.Count)
        {
            errors["files"] = ["The same file identity appears more than once in the batch."];
        }

        for (var index = 0; index < request.Files.Count; index++)
        {
            var file = request.Files[index];
            if (file.ClientFileId is null
                || file.ClientFileId.Length != 64
                || !file.ClientFileId.All(Uri.IsHexDigit))
            {
                errors[$"files[{index}].clientFileId"] =
                [
                    "The file identity must be a SHA-256 hex value.",
                ];
            }

            if (file.SizeBytes is <= 0 or > PhotoStorageNames.MaximumFileSizeBytes)
            {
                errors[$"files[{index}].sizeBytes"] =
                [
                    "The file must be larger than 0 bytes and at most 100 MB.",
                ];
            }

            if (!SupportedPhotoMedia.TryNormalize(
                    file.FileName,
                    file.ContentType,
                    out _,
                    out _,
                    out _))
            {
                errors[$"files[{index}]"] =
                [
                    "Only JPEG (.jpg/.jpeg) and HEIC/HEIF (.heic/.heif) are supported.",
                ];
            }
        }

        return errors;
    }
}
