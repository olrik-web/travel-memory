using Microsoft.EntityFrameworkCore;
using TravelMemory.Persistence.Data;

namespace TravelMemory.Api;

// Applies pending EF Core migrations and exits. In Azure this runs as a one-off job during
// deployment rather than on every cold start of the API, and from inside Azure, so the
// database firewall does not have to admit GitHub's runners. It builds a small host with
// only the database, so the job needs no sign-in or storage configuration.
internal static class DatabaseMigration
{
    public const string Argument = "--migrate";

    public static async Task<int> RunAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(
            args.Where(arg => arg != Argument).ToArray());
        builder.AddServiceDefaults();
        builder.AddSqlServerDbContext<TravelMemoryDbContext>("travelmemory");

        using var host = builder.Build();
        // Starting and stopping the host also starts and flushes telemetry for the run.
        await host.StartAsync();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<TravelMemoryDbContext>()
                .Database
                .MigrateAsync();
        }

        await host.StopAsync();
        return 0;
    }
}
