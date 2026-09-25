using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace TravelMemory.Api.Tests.Integration;

internal sealed class TravelMemoryApplicationFactory(
    string databaseConnectionString,
    string storageConnectionString,
    Guid ownerId,
    Action<IServiceCollection>? configureTestServices = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:travelmemory", databaseConnectionString);
        builder.UseSetting("ConnectionStrings:blobs", storageConnectionString);
        builder.UseSetting("ConnectionStrings:queues", storageConnectionString);
        builder.UseSetting("Identity:DevelopmentOwnerId", ownerId.ToString());
        if (configureTestServices is not null)
        {
            builder.ConfigureTestServices(configureTestServices);
        }
    }
}
