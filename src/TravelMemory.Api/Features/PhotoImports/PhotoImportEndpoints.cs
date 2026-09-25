using System.Data;
using Azure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TravelMemory.Api.Auth;
using TravelMemory.Domain.Photos;
using TravelMemory.Persistence.Data;
using TravelMemory.Persistence.Photos;

namespace TravelMemory.Api.Features.PhotoImports;

internal static class PhotoImportEndpoints
{
    public static IEndpointRouteBuilder MapPhotoImportEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var imports = endpoints.MapGroup("/api/photo-imports")
            .RequireAuthorization()
            .WithTags("Photo imports");

        endpoints.MapPost("/api/trips/{tripId:guid}/photo-imports", CreateBatchAsync)
            .RequireAuthorization()
            .WithTags("Photo imports")
            .WithName("CreatePhotoImport")
            .ProducesValidationProblem();
        imports.MapGet("/{batchId:guid}", GetBatchAsync)
            .WithName("GetPhotoImport");
        imports.MapGet("/{batchId:guid}/time-preview", PreviewTimeAdjustmentAsync)
            .WithName("PreviewPhotoImportTimeAdjustment");
        imports.MapPost("/{batchId:guid}/finalize", FinalizeBatchAsync)
            .WithName("FinalizePhotoImport");
        imports.MapPost(
                "/{batchId:guid}/items/{itemId:guid}/upload-token",
                RenewUploadTokenAsync)
            .WithName("RenewPhotoUploadToken");
        imports.MapPost(
                "/{batchId:guid}/items/{itemId:guid}/complete-upload",
                CompleteUploadAsync)
            .WithName("CompletePhotoUpload");
        imports.MapPost(
                "/{batchId:guid}/items/{itemId:guid}/retry",
                RetryItemAsync)
            .WithName("RetryPhotoImportItem");

        endpoints.MapGet("/api/trips/{tripId:guid}/photos", GetTimelineAsync)
            .RequireAuthorization()
            .WithTags("Photos")
            .WithName("GetTripPhotoTimeline");

        return endpoints;
    }

    private static async Task<IResult> CreateBatchAsync(
        Guid tripId,
        CreatePhotoImportRequest request,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        PhotoStorage storage,
        TravelMemoryDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var errors = ValidateCreateRequest(request);
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

        async Task<IResult> CreateWithinTransactionAsync()
        {
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
                        includeUploadGrants: true));
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
                    $"owners/{currentUser.OwnerId:N}/trips/{tripId:N}/imports/{batch.Id:N}/{Guid.NewGuid():N}{extension}";
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
                    includeUploadGrants: true));
        }
    }

    private static async Task<IResult> GetBatchAsync(
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

    private static async Task<IResult> RenewUploadTokenAsync(
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

    private static async Task<IResult> CompleteUploadAsync(
        Guid batchId,
        Guid itemId,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        PhotoStorage storage,
        TravelMemoryDbContext dbContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.PhotoImportItems.SingleOrDefaultAsync(
            value =>
                value.Id == itemId
                && value.ImportBatchId == batchId
                && value.OwnerId == currentUser.OwnerId,
            cancellationToken);
        if (item is null)
        {
            return TypedResults.NotFound();
        }

        var batch = await dbContext.PhotoImportBatches.SingleAsync(
            value => value.Id == batchId && value.OwnerId == currentUser.OwnerId,
            cancellationToken);
        var items = await dbContext.PhotoImportItems
            .Where(value => value.ImportBatchId == batchId)
            .ToListAsync(cancellationToken);

        if (item.State != PhotoImportItemState.AwaitingUpload)
        {
            return TypedResults.Accepted(
                $"/api/photo-imports/{batch.Id}",
                PhotoImportResponseFactory.Create(
                    batch,
                    items,
                    storage,
                    includeUploadGrants: false));
        }

        if (!await storage.VerifyUploadAsync(
                item.TemporaryBlobName,
                item.ExpectedSizeBytes,
                cancellationToken))
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["file"] =
                    [
                        "The upload is missing or has the wrong size. Upload the file again.",
                    ],
                });
        }

        var now = timeProvider.GetUtcNow();
        item.QueueForAnalysis(now);
        var job = PhotoProcessingJob.Create(item, PhotoProcessingJobKind.Analyze, now);
        dbContext.PhotoProcessingJobs.Add(job);
        batch.SetState(PhotoImportBatchStateCalculator.Calculate(batch, items), now);
        await dbContext.SaveChangesAsync(cancellationToken);

        var dispatched = await TryDispatchAsync(
            job,
            storage,
            loggerFactory.CreateLogger("PhotoImportDispatch"),
            cancellationToken);
        if (!dispatched)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "The photo is saved, but the processing queue is temporarily unavailable.",
                detail: "Try completing the upload again. No new job is created.");
        }

        return TypedResults.Accepted(
            $"/api/photo-imports/{batch.Id}",
            PhotoImportResponseFactory.Create(
                batch,
                items,
                storage,
                includeUploadGrants: false));
    }

    private static async Task<IResult> PreviewTimeAdjustmentAsync(
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

    private static async Task<IResult> FinalizeBatchAsync(
        Guid batchId,
        FinalizePhotoImportRequest request,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        PhotoStorage storage,
        TravelMemoryDbContext dbContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (request.TimeAdjustmentMinutes is
            < -PhotoImportBatch.MaximumAdjustmentMinutes
            or > PhotoImportBatch.MaximumAdjustmentMinutes)
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["timeAdjustmentMinutes"] =
                    [
                        "The time adjustment must be between -24 and +24 hours.",
                    ],
                });
        }

        var batch = await dbContext.PhotoImportBatches.SingleOrDefaultAsync(
            value => value.Id == batchId && value.OwnerId == currentUser.OwnerId,
            cancellationToken);
        if (batch is null)
        {
            return TypedResults.NotFound();
        }

        var items = await dbContext.PhotoImportItems
            .Where(item => item.ImportBatchId == batch.Id)
            .ToListAsync(cancellationToken);

        if (batch.TimeAdjustmentMinutes is not null)
        {
            if (batch.TimeAdjustmentMinutes != request.TimeAdjustmentMinutes)
            {
                return TypedResults.Conflict(
                    "The batch is already finalized with a different time adjustment.");
            }

            var pendingJobs = await dbContext.PhotoProcessingJobs
                .Where(job =>
                    job.ImportBatchId == batch.Id
                    && job.State == PhotoProcessingJobState.Pending)
                .ToListAsync(cancellationToken);
            var redispatched = await DispatchAllAsync(
                pendingJobs,
                storage,
                loggerFactory,
                cancellationToken);

            return redispatched
                ? TypedResults.Accepted(
                    $"/api/photo-imports/{batch.Id}",
                    PhotoImportResponseFactory.Create(
                        batch,
                        items,
                        storage,
                        includeUploadGrants: false))
                : TypedResults.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "The processing queue is temporarily unavailable.");
        }

        if (batch.State != PhotoImportBatchState.ReadyForReview)
        {
            return TypedResults.Conflict(
                "The batch must finish analysis before the time adjustment can be locked.");
        }

        var now = timeProvider.GetUtcNow();
        batch.SetTimeAdjustment(request.TimeAdjustmentMinutes, now);
        var jobs = new List<PhotoProcessingJob>();
        foreach (var item in items.Where(
                     item => item.State == PhotoImportItemState.ReadyForReview))
        {
            item.QueueForProcessing(now);
            var job = PhotoProcessingJob.Create(item, PhotoProcessingJobKind.Process, now);
            jobs.Add(job);
            dbContext.PhotoProcessingJobs.Add(job);
        }

        batch.SetState(PhotoImportBatchStateCalculator.Calculate(batch, items), now);
        await dbContext.SaveChangesAsync(cancellationToken);

        var dispatched = await DispatchAllAsync(
            jobs,
            storage,
            loggerFactory,
            cancellationToken);
        if (!dispatched)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "The batch is saved, but the processing queue is temporarily unavailable.",
                detail: "Finalize again to resend the same idempotent jobs.");
        }

        return TypedResults.Accepted(
            $"/api/photo-imports/{batch.Id}",
            PhotoImportResponseFactory.Create(
                batch,
                items,
                storage,
                includeUploadGrants: false));
    }

    private static async Task<IResult> RetryItemAsync(
        Guid batchId,
        Guid itemId,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        PhotoStorage storage,
        TravelMemoryDbContext dbContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.PhotoImportItems.SingleOrDefaultAsync(
            value =>
                value.Id == itemId
                && value.ImportBatchId == batchId
                && value.OwnerId == currentUser.OwnerId,
            cancellationToken);
        if (item is null)
        {
            return TypedResults.NotFound();
        }

        if (!PhotoImportResponseFactory.CanRetry(item))
        {
            return TypedResults.Conflict(
                "This failure cannot be retried. Select the file again in a new import.");
        }

        var job = await dbContext.PhotoProcessingJobs
            .Where(value =>
                value.ImportItemId == item.Id
                && value.State == PhotoProcessingJobState.Failed)
            .OrderByDescending(value => value.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (job is null)
        {
            return TypedResults.Conflict("There is no failed job for this file.");
        }

        var batch = await dbContext.PhotoImportBatches.SingleAsync(
            value => value.Id == batchId,
            cancellationToken);
        var allItems = await dbContext.PhotoImportItems
            .Where(value => value.ImportBatchId == batchId)
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        job.ResetForManualRetry(now);
        var expirationJobs = await dbContext.PhotoProcessingJobs
            .Where(value =>
                value.ImportItemId == item.Id
                && value.Kind == PhotoProcessingJobKind.ExpireFailedOriginal
                && value.State == PhotoProcessingJobState.Pending)
            .ToListAsync(cancellationToken);
        foreach (var expirationJob in expirationJobs)
        {
            expirationJob.Succeed(now);
        }

        item.ClearFailedOriginalRetention();
        if (job.Kind == PhotoProcessingJobKind.Cleanup)
        {
            item.MarkCleanupPending(item.Outcome, now, now);
        }
        else if (job.Kind == PhotoProcessingJobKind.Analyze)
        {
            item.QueueForAnalysis(now);
        }
        else
        {
            item.QueueForProcessing(now);
        }

        batch.SetState(PhotoImportBatchStateCalculator.Calculate(batch, allItems), now);
        await dbContext.SaveChangesAsync(cancellationToken);

        return await TryDispatchAsync(
                job,
                storage,
                loggerFactory.CreateLogger("PhotoImportDispatch"),
                cancellationToken)
            ? TypedResults.Accepted($"/api/photo-imports/{batchId}")
            : TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "The processing queue is temporarily unavailable.");
    }

    private static async Task<IResult> GetTimelineAsync(
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
        var timeline = photos.Select(photo =>
        {
            var thumbnail = storage.CreateReadGrant(photo.ThumbnailBlobName);
            var web = storage.CreateReadGrant(photo.WebBlobName);
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

    private static Dictionary<string, string[]> ValidateCreateRequest(
        CreatePhotoImportRequest request)
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

    private static async Task<bool> DispatchAllAsync(
        IReadOnlyCollection<PhotoProcessingJob> jobs,
        PhotoStorage storage,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("PhotoImportDispatch");
        var success = true;
        foreach (var job in jobs)
        {
            success &= await TryDispatchAsync(
                job,
                storage,
                logger,
                cancellationToken);
        }

        return success;
    }

    private static async Task<bool> TryDispatchAsync(
        PhotoProcessingJob job,
        PhotoStorage storage,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await storage.EnqueueAsync(job.Id, cancellationToken);
            return true;
        }
        catch (RequestFailedException exception)
        {
            logger.LogError(
                exception,
                "Could not enqueue photo processing job {JobId}. The database job remains pending.",
                job.Id);
            return false;
        }
    }
}
