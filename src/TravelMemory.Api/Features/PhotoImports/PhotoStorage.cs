using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Azure.Storage.Queues;
using System.Text.Json;
using TravelMemory.Domain.Photos;
using TravelMemory.Persistence.Photos;

namespace TravelMemory.Api.Features.PhotoImports;

internal sealed class PhotoStorage(
    BlobServiceClient blobServiceClient,
    QueueServiceClient queueServiceClient,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan SasLifetime = TimeSpan.FromMinutes(15);

    public async Task InitializeAsync(bool configureDevelopmentCors, CancellationToken cancellationToken)
    {
        await blobServiceClient
            .GetBlobContainerClient(PhotoStorageNames.TemporaryContainer)
            .CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        await blobServiceClient
            .GetBlobContainerClient(PhotoStorageNames.PermanentContainer)
            .CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        await queueServiceClient
            .GetQueueClient(PhotoStorageNames.ProcessingQueue)
            .CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        if (configureDevelopmentCors)
        {
            var properties = (await blobServiceClient.GetPropertiesAsync(cancellationToken)).Value;
            properties.Cors.Clear();
            properties.Cors.Add(
                new BlobCorsRule
                {
                    AllowedOrigins = "*",
                    AllowedMethods = "GET,PUT,OPTIONS",
                    AllowedHeaders = "*",
                    ExposedHeaders = "ETag,x-ms-request-id",
                    MaxAgeInSeconds = 3600,
                });
            await blobServiceClient.SetPropertiesAsync(properties, cancellationToken);
        }
    }

    public UploadGrantResponse CreateUploadGrant(string blobName)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(SasLifetime);
        var blob = blobServiceClient
            .GetBlobContainerClient(PhotoStorageNames.TemporaryContainer)
            .GetBlobClient(blobName);

        if (!blob.CanGenerateSasUri)
        {
            throw new InvalidOperationException(
                "The configured Blob client cannot create upload SAS tokens.");
        }

        var sas = new BlobSasBuilder
        {
            BlobContainerName = PhotoStorageNames.TemporaryContainer,
            BlobName = blobName,
            Resource = "b",
            StartsOn = now.AddMinutes(-1),
            ExpiresOn = expiresAt,
            Protocol = SasProtocol.HttpsAndHttp,
        };
        sas.SetPermissions(BlobSasPermissions.Create | BlobSasPermissions.Write);

        return new UploadGrantResponse(blob.GenerateSasUri(sas), expiresAt);
    }

    public (Uri Url, DateTimeOffset ExpiresAtUtc) CreateReadGrant(string blobName)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(SasLifetime);
        var blob = blobServiceClient
            .GetBlobContainerClient(PhotoStorageNames.PermanentContainer)
            .GetBlobClient(blobName);

        if (!blob.CanGenerateSasUri)
        {
            throw new InvalidOperationException(
                "The configured Blob client cannot create read SAS tokens.");
        }

        var sas = new BlobSasBuilder
        {
            BlobContainerName = PhotoStorageNames.PermanentContainer,
            BlobName = blobName,
            Resource = "b",
            StartsOn = now.AddMinutes(-1),
            ExpiresOn = expiresAt,
            Protocol = SasProtocol.HttpsAndHttp,
        };
        sas.SetPermissions(BlobSasPermissions.Read);

        return (blob.GenerateSasUri(sas), expiresAt);
    }

    public async Task<bool> VerifyUploadAsync(
        string blobName,
        long expectedSizeBytes,
        CancellationToken cancellationToken)
    {
        var blob = blobServiceClient
            .GetBlobContainerClient(PhotoStorageNames.TemporaryContainer)
            .GetBlobClient(blobName);

        try
        {
            var properties = await blob.GetPropertiesAsync(cancellationToken: cancellationToken);
            return properties.Value.ContentLength == expectedSizeBytes;
        }
        catch (RequestFailedException exception) when (exception.Status == StatusCodes.Status404NotFound)
        {
            return false;
        }
    }

    public Task EnqueueAsync(PhotoProcessingJob job, CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Serialize(new PhotoQueueMessage(job.Id, job.TraceParent));
        return queueServiceClient
            .GetQueueClient(PhotoStorageNames.ProcessingQueue)
            .SendMessageAsync(message, cancellationToken);
    }
}
