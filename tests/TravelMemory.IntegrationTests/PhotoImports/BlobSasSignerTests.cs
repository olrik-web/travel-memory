using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.AspNetCore.WebUtilities;
using TravelMemory.Api.Features.PhotoImports;

namespace TravelMemory.IntegrationTests.PhotoImports;

public sealed class BlobSasSignerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Signs_with_the_user_delegation_key_and_https_only_in_azure()
    {
        // Object id, tenant id, validity, service, version, and key value, in that order;
        // named arguments are ambiguous between the factory's two overloads.
        var key = BlobsModelFactory.UserDelegationKey(
            "11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222",
            Now.AddMinutes(-1),
            Now.AddHours(6),
            "b",
            "2025-11-05",
            Convert.ToBase64String(new byte[32]));
        var blob = new BlobClient(new Uri("https://travelmemory.blob.core.windows.net/photos/web.jpg"));

        var query = QueryHelpers.ParseQuery(
            new BlobSasSigner("travelmemory", key).Sign(blob, CreateReadSas()).Query);

        Assert.Equal("11111111-1111-1111-1111-111111111111", query["skoid"].ToString());
        Assert.Equal("https", query["spr"].ToString());
        Assert.False(string.IsNullOrEmpty(query["sig"]));
    }

    [Fact]
    public void Signs_with_the_account_key_and_allows_http_against_azurite()
    {
        var blob = new BlobClient(
            new Uri("http://127.0.0.1:10000/devstoreaccount1/photos/web.jpg"),
            new StorageSharedKeyCredential("devstoreaccount1", Convert.ToBase64String(new byte[64])));

        var query = QueryHelpers.ParseQuery(
            new BlobSasSigner("devstoreaccount1", delegationKey: null).Sign(blob, CreateReadSas()).Query);

        Assert.False(query.ContainsKey("skoid"));
        Assert.Equal("https,http", query["spr"].ToString());
    }

    private static BlobSasBuilder CreateReadSas()
    {
        var sas = new BlobSasBuilder
        {
            BlobContainerName = "photos",
            BlobName = "web.jpg",
            Resource = "b",
            StartsOn = Now.AddMinutes(-1),
            ExpiresOn = Now.AddMinutes(15),
        };
        sas.SetPermissions(BlobSasPermissions.Read);
        return sas;
    }
}
