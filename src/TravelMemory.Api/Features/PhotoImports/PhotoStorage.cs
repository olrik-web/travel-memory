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
    private static readonly TimeSpan DelegationKeyLifetime = TimeSpan.FromHours(6);

    // Shared by all requests, so a timeline with many photos costs at most one key request
    // per few hours. Concurrent requests may each fetch a key when it expires; any of them
    // is valid, so no lock is needed.
    private UserDelegationKey? delegationKey;

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

    public async Task<BlobSasSigner> GetSasSignerAsync(CancellationToken cancellationToken)
    {
        if (blobServiceClient.CanGenerateAccountSasUri)
        {
            return new BlobSasSigner(blobServiceClient.AccountName, delegationKey: null);
        }

        // A token must not outlive the key that signed it.
        var now = timeProvider.GetUtcNow();
        var key = delegationKey;
        if (key is null || key.SignedExpiresOn < now.Add(SasLifetime).AddMinutes(1))
        {
            key = (await blobServiceClient.GetUserDelegationKeyAsync(
                now.AddMinutes(-1),
                now.Add(DelegationKeyLifetime),
                cancellationToken)).Value;
            delegationKey = key;
        }

        return new BlobSasSigner(blobServiceClient.AccountName, key);
    }

    public UploadGrantResponse CreateUploadGrant(BlobSasSigner signer, string blobName)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(SasLifetime);
        var blob = blobServiceClient
            .GetBlobContainerClient(PhotoStorageNames.TemporaryContainer)
            .GetBlobClient(blobName);

        var sas = new BlobSasBuilder
        {
            BlobContainerName = PhotoStorageNames.TemporaryContainer,
            BlobName = blobName,
            Resource = "b",
            StartsOn = now.AddMinutes(-1),
            ExpiresOn = expiresAt,
        };
        sas.SetPermissions(BlobSasPermissions.Create | BlobSasPermissions.Write);

        return new UploadGrantResponse(signer.Sign(blob, sas), expiresAt);
    }

    public (Uri Url, DateTimeOffset ExpiresAtUtc) CreateReadGrant(
        BlobSasSigner signer,
        string blobName)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(SasLifetime);
        var blob = blobServiceClient
            .GetBlobContainerClient(PhotoStorageNames.PermanentContainer)
            .GetBlobClient(blobName);

        var sas = new BlobSasBuilder
        {
            BlobContainerName = PhotoStorageNames.PermanentContainer,
            BlobName = blobName,
            Resource = "b",
            StartsOn = now.AddMinutes(-1),
            ExpiresOn = expiresAt,
        };
        sas.SetPermissions(BlobSasPermissions.Read);

        return (signer.Sign(blob, sas), expiresAt);
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
