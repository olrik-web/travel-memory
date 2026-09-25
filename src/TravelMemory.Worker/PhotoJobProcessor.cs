using System.Security.Cryptography;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using TravelMemory.Domain.Photos;
using TravelMemory.Persistence.Data;
using TravelMemory.Persistence.Photos;

namespace TravelMemory.Worker;

internal sealed class PhotoJobProcessor(
    TravelMemoryDbContext dbContext,
    BlobServiceClient blobServiceClient,
    PhotoImageProcessor imageProcessor,
    TimeProvider timeProvider,
    ILogger<PhotoJobProcessor> logger)
{
    private static readonly TimeSpan FailedOriginalRetention = TimeSpan.FromDays(7);
    private static readonly SemaphoreSlim PhotoPersistenceLock = new(1, 1);

    public async Task ProcessAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await dbContext.PhotoProcessingJobs.SingleOrDefaultAsync(
            value => value.Id == jobId,
            cancellationToken);
        if (job is null
            || job.State is PhotoProcessingJobState.Succeeded
                or PhotoProcessingJobState.Failed)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        if (!job.TryStart(now))
        {
            return;
        }

        var item = await dbContext.PhotoImportItems.SingleAsync(
            value => value.Id == job.ImportItemId,
            cancellationToken);
        var batch = await dbContext.PhotoImportBatches.SingleAsync(
            value => value.Id == job.ImportBatchId,
            cancellationToken);

        SetProcessingState(job.Kind, item, now);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            switch (job.Kind)
            {
                case PhotoProcessingJobKind.Analyze:
                    await AnalyzeAsync(item, cancellationToken);
                    break;
                case PhotoProcessingJobKind.Process:
                    await ProcessPhotoAsync(batch, item, cancellationToken);
                    break;
                case PhotoProcessingJobKind.Cleanup:
                    await CleanupAsync(item, cancellationToken);
                    break;
                case PhotoProcessingJobKind.ExpireFailedOriginal:
                    await ExpireFailedOriginalAsync(item, cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown job kind {job.Kind}.");
            }

            job.Succeed(timeProvider.GetUtcNow());
            await UpdateBatchStateAsync(batch, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (PhotoProcessingException exception)
        {
            await HandleFailureAsync(job, item, batch, exception, cancellationToken);
        }
        catch (RequestFailedException exception) when (
            exception.Status is 408 or 429 or >= 500)
        {
            await HandleFailureAsync(
                job,
                item,
                batch,
                new PhotoProcessingException(
                    "temporary_failure",
                    "Storage did not respond. The photo will be reprocessed automatically.",
                    isTransient: true,
                    exception),
                cancellationToken);
        }
        catch (IOException exception)
        {
            await HandleFailureAsync(
                job,
                item,
                batch,
                new PhotoProcessingException(
                    "temporary_failure",
                    "En midlertidig filfejl opstod. Fotoet genbehandles automatisk.",
                    isTransient: true,
                    exception),
                cancellationToken);
        }
    }

    private async Task AnalyzeAsync(
        PhotoImportItem item,
        CancellationToken cancellationToken)
    {
        if (item.ContentHash is not null && item.CapturedAtOriginalLocal is not null)
        {
            item.MarkReadyForReview(
                item.ContentHash,
                item.CapturedAtOriginalLocal.Value,
                item.ExifOffsetMinutes,
                timeProvider.GetUtcNow());
            return;
        }

        var temporaryPath = CreateTemporaryPath(item.Id, "source");
        try
        {
            await GetTemporaryBlob(item)
                .DownloadToAsync(temporaryPath, cancellationToken);
            var contentHash = await ComputeHashAsync(temporaryPath, cancellationToken);

            var existingPhoto = await dbContext.Photos.AnyAsync(
                photo =>
                    photo.OwnerId == item.OwnerId
                    && photo.TripId == item.TripId
                    && photo.ContentHash == contentHash,
                cancellationToken);
            if (existingPhoto)
            {
                item.MarkCleanupPending(
                    PhotoImportItemOutcome.Duplicate,
                    timeProvider.GetUtcNow(),
                    timeProvider.GetUtcNow());
                await EnsureCleanupJobAsync(item, cancellationToken);
                return;
            }

            var metadata = imageProcessor.ReadMetadata(temporaryPath);
            item.MarkReadyForReview(
                contentHash,
                metadata.CapturedAtOriginalLocal,
                metadata.ExifOffsetMinutes,
                timeProvider.GetUtcNow());
        }
        finally
        {
            DeleteTemporaryPath(temporaryPath);
        }
    }

    private async Task ProcessPhotoAsync(
        PhotoImportBatch batch,
        PhotoImportItem item,
        CancellationToken cancellationToken)
    {
        if (batch.TimeAdjustmentMinutes is null)
        {
            throw new InvalidOperationException("The import batch has no time adjustment.");
        }

        var existingForItem = await dbContext.Photos.SingleOrDefaultAsync(
            photo => photo.ImportItemId == item.Id,
            cancellationToken);
        if (existingForItem is not null)
        {
            item.MarkCleanupPending(
                PhotoImportItemOutcome.Imported,
                timeProvider.GetUtcNow(),
                timeProvider.GetUtcNow());
            await EnsureCleanupJobAsync(item, cancellationToken);
            return;
        }

        if (item.ContentHash is null)
        {
            throw new InvalidOperationException("The import item has no content hash.");
        }

        var duplicate = await dbContext.Photos.AnyAsync(
            photo =>
                photo.OwnerId == item.OwnerId
                && photo.TripId == item.TripId
                && photo.ContentHash == item.ContentHash,
            cancellationToken);
        if (duplicate)
        {
            item.MarkCleanupPending(
                PhotoImportItemOutcome.Duplicate,
                timeProvider.GetUtcNow(),
                timeProvider.GetUtcNow());
            await EnsureCleanupJobAsync(item, cancellationToken);
            return;
        }

        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "travel-memory",
            item.Id.ToString("N"));
        var sourcePath = Path.Combine(temporaryDirectory, "source");
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            await GetTemporaryBlob(item).DownloadToAsync(sourcePath, cancellationToken);
            var processed = imageProcessor.Process(sourcePath, temporaryDirectory);
            var blobPrefix =
                $"owners/{item.OwnerId:N}/trips/{item.TripId:N}/photos/{item.Id:N}";
            var webBlobName = $"{blobPrefix}/web.jpg";
            var thumbnailBlobName = $"{blobPrefix}/thumbnail.jpg";

            await UploadDerivativeAsync(
                webBlobName,
                processed.WebPath,
                cancellationToken);
            await UploadDerivativeAsync(
                thumbnailBlobName,
                processed.ThumbnailPath,
                cancellationToken);
            await VerifyDerivativeAsync(webBlobName, cancellationToken);
            await VerifyDerivativeAsync(thumbnailBlobName, cancellationToken);

            await PhotoPersistenceLock.WaitAsync(cancellationToken);
            try
            {
                var duplicateAfterProcessing = await dbContext.Photos.AnyAsync(
                    photo =>
                        photo.OwnerId == item.OwnerId
                        && photo.TripId == item.TripId
                        && photo.ContentHash == item.ContentHash,
                    cancellationToken);

                if (duplicateAfterProcessing)
                {
                    await DeleteDerivativeAsync(webBlobName, cancellationToken);
                    await DeleteDerivativeAsync(thumbnailBlobName, cancellationToken);
                    item.MarkCleanupPending(
                        PhotoImportItemOutcome.Duplicate,
                        timeProvider.GetUtcNow(),
                        timeProvider.GetUtcNow());
                }
                else
                {
                    dbContext.Photos.Add(
                        Photo.Create(
                            item,
                            batch.TimeAdjustmentMinutes.Value,
                            webBlobName,
                            thumbnailBlobName,
                            processed.Width,
                            processed.Height,
                            processed.ThumbnailWidth,
                            processed.ThumbnailHeight,
                            timeProvider.GetUtcNow()));
                    item.MarkCleanupPending(
                        PhotoImportItemOutcome.Imported,
                        timeProvider.GetUtcNow(),
                        timeProvider.GetUtcNow());
                }

                await EnsureCleanupJobAsync(item, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                PhotoPersistenceLock.Release();
            }
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryDirectory);
        }
    }

    private async Task CleanupAsync(
        PhotoImportItem item,
        CancellationToken cancellationToken)
    {
        await GetTemporaryBlob(item).DeleteIfExistsAsync(
            DeleteSnapshotsOption.IncludeSnapshots,
            cancellationToken: cancellationToken);
        item.MarkSucceeded(timeProvider.GetUtcNow());
    }

    private async Task ExpireFailedOriginalAsync(
        PhotoImportItem item,
        CancellationToken cancellationToken)
    {
        await GetTemporaryBlob(item).DeleteIfExistsAsync(
            DeleteSnapshotsOption.IncludeSnapshots,
            cancellationToken: cancellationToken);
        item.MarkFailedOriginalDeleted(timeProvider.GetUtcNow());
    }

    private async Task HandleFailureAsync(
        PhotoProcessingJob job,
        PhotoImportItem item,
        PhotoImportBatch batch,
        PhotoProcessingException exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(
            exception,
            "Photo processing job {JobId} failed on attempt {AttemptCount}.",
            job.Id,
            job.AttemptCount);
        var now = timeProvider.GetUtcNow();

        if (exception.IsTransient && job.AttemptCount < PhotoProcessingJob.MaximumAttempts)
        {
            var delay = TimeSpan.FromSeconds(Math.Pow(2, job.AttemptCount) * 5);
            job.Retry(exception.Message, now, delay);
            SetQueuedState(job.Kind, item, now);
        }
        else
        {
            job.Fail(exception.Message, now);
            if (job.Kind == PhotoProcessingJobKind.Cleanup)
            {
                item.MarkCleanupFailed(
                    "cleanup_failed",
                    "Derivatives and metadata are saved, but the original could not be deleted. Retry the cleanup.",
                    now);
            }
            else
            {
                item.MarkFailed(
                    exception.IsTransient ? "processing_exhausted" : exception.Code,
                    exception.UserMessage,
                    now);
                var retainedUntil = now.Add(FailedOriginalRetention);
                item.RetainFailedOriginalUntil(retainedUntil);
                await EnsureFailedOriginalExpirationJobAsync(
                    item,
                    now,
                    retainedUntil,
                    cancellationToken);
            }
        }

        await UpdateBatchStateAsync(batch, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureCleanupJobAsync(
        PhotoImportItem item,
        CancellationToken cancellationToken)
    {
        var exists = dbContext.ChangeTracker
            .Entries<PhotoProcessingJob>()
            .Any(entry =>
                entry.State == EntityState.Added
                && entry.Entity.ImportItemId == item.Id
                && entry.Entity.Kind == PhotoProcessingJobKind.Cleanup)
            || await dbContext.PhotoProcessingJobs.AnyAsync(
            job =>
                job.ImportItemId == item.Id
                && job.Kind == PhotoProcessingJobKind.Cleanup,
            cancellationToken);
        if (!exists)
        {
            dbContext.PhotoProcessingJobs.Add(
                PhotoProcessingJob.Create(
                    item,
                    PhotoProcessingJobKind.Cleanup,
                    timeProvider.GetUtcNow()));
        }
    }

    private async Task EnsureFailedOriginalExpirationJobAsync(
        PhotoImportItem item,
        DateTimeOffset createdAt,
        DateTimeOffset availableAt,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.PhotoProcessingJobs.AnyAsync(
            job =>
                job.ImportItemId == item.Id
                && job.Kind == PhotoProcessingJobKind.ExpireFailedOriginal,
            cancellationToken);
        if (!exists)
        {
            dbContext.PhotoProcessingJobs.Add(
                PhotoProcessingJob.Schedule(
                    item,
                    PhotoProcessingJobKind.ExpireFailedOriginal,
                    createdAt,
                    availableAt));
        }
    }

    private async Task UpdateBatchStateAsync(
        PhotoImportBatch batch,
        CancellationToken cancellationToken)
    {
        var items = await dbContext.PhotoImportItems
            .Where(item => item.ImportBatchId == batch.Id)
            .ToListAsync(cancellationToken);
        batch.SetState(
            PhotoImportBatchStateCalculator.Calculate(batch, items),
            timeProvider.GetUtcNow());
    }

    private BlobClient GetTemporaryBlob(
        PhotoImportItem item) =>
        blobServiceClient
            .GetBlobContainerClient(PhotoStorageNames.TemporaryContainer)
            .GetBlobClient(item.TemporaryBlobName);

    private async Task UploadDerivativeAsync(
        string blobName,
        string filePath,
        CancellationToken cancellationToken)
    {
        var blob = blobServiceClient
            .GetBlobContainerClient(PhotoStorageNames.PermanentContainer)
            .GetBlobClient(blobName);
        await blob.UploadAsync(
            filePath,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = "image/jpeg",
                    CacheControl = "private, max-age=31536000, immutable",
                },
            },
            cancellationToken);
    }

    private async Task VerifyDerivativeAsync(
        string blobName,
        CancellationToken cancellationToken)
    {
        var properties = await blobServiceClient
            .GetBlobContainerClient(PhotoStorageNames.PermanentContainer)
            .GetBlobClient(blobName)
            .GetPropertiesAsync(cancellationToken: cancellationToken);
        if (properties.Value.ContentLength <= 0
            || properties.Value.ContentType != "image/jpeg")
        {
            throw new PhotoProcessingException(
                "derivative_verification_failed",
                "One of the processed images could not be verified. The photo will be reprocessed automatically.",
                isTransient: true);
        }
    }

    private Task DeleteDerivativeAsync(
        string blobName,
        CancellationToken cancellationToken) =>
        blobServiceClient
            .GetBlobContainerClient(PhotoStorageNames.PermanentContainer)
            .GetBlobClient(blobName)
            .DeleteIfExistsAsync(cancellationToken: cancellationToken);

    private static async Task<string> ComputeHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static string CreateTemporaryPath(Guid itemId, string suffix)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "travel-memory",
            itemId.ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, suffix);
    }

    private static void DeleteTemporaryPath(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var directory = Path.GetDirectoryName(path);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void SetProcessingState(
        PhotoProcessingJobKind kind,
        PhotoImportItem item,
        DateTimeOffset now)
    {
        if (kind == PhotoProcessingJobKind.Analyze)
        {
            item.MarkAnalyzing(now);
        }
        else if (kind == PhotoProcessingJobKind.Process)
        {
            item.MarkProcessing(now);
        }
    }

    private static void SetQueuedState(
        PhotoProcessingJobKind kind,
        PhotoImportItem item,
        DateTimeOffset now)
    {
        if (kind == PhotoProcessingJobKind.Analyze)
        {
            item.QueueForAnalysis(now);
        }
        else if (kind == PhotoProcessingJobKind.Process)
        {
            item.QueueForProcessing(now);
        }
    }
}
