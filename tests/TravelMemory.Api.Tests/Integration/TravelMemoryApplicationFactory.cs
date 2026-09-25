using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace TravelMemory.Api.Tests.Integration;

// Without an owner id, the real cookie scheme stays in place and requests are anonymous.
internal sealed class TravelMemoryApplicationFactory(
    string databaseConnectionString,
    string storageConnectionString,
    Guid? ownerId,
    Action<IServiceCollection>? configureTestServices = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:travelmemory", databaseConnectionString);
        builder.UseSetting("ConnectionStrings:blobs", storageConnectionString);
        builder.UseSetting("ConnectionStrings:queues", storageConnectionString);
        builder.UseSetting(
            "Authentication:Oidc:Authority",
            "https://identity.test/realms/travel-memory");
        builder.UseSetting("Authentication:Oidc:ClientId", "travel-memory-web");
        builder.UseSetting("Authentication:Oidc:ClientSecret", "test-client-secret");
        builder.ConfigureTestServices(services =>
        {
            if (ownerId is not null)
            {
                services
                    .AddAuthentication(TestAuthenticationHandler.SchemeName)
                    .AddScheme<TestAuthenticationOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        options => options.OwnerId = ownerId.Value);
            }

            configureTestServices?.Invoke(services);
        });
    }
}
