using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace TravelMemory.Api.Features.PhotoImports;

// Signs SAS tokens. Locally the Blob client holds the Azurite account key. In Azure it
// authenticates with a managed identity and has no key, so tokens are signed with a user
// delegation key obtained through that identity instead.
internal sealed class BlobSasSigner(string accountName, UserDelegationKey? delegationKey)
{
    public Uri Sign(BlobClient blob, BlobSasBuilder sas)
    {
        // Plain HTTP is only allowed against a plain-HTTP endpoint such as Azurite.
        sas.Protocol = blob.Uri.Scheme == Uri.UriSchemeHttps
            ? SasProtocol.Https
            : SasProtocol.HttpsAndHttp;

        return delegationKey is null
            ? blob.GenerateSasUri(sas)
            : new BlobUriBuilder(blob.Uri)
            {
                Sas = sas.ToSasQueryParameters(delegationKey, accountName),
            }.ToUri();
    }
}
