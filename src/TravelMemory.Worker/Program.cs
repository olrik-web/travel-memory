using TravelMemory.Persistence.Data;

namespace TravelMemory.Worker;

internal static class WorkerProgram
{
    // Runs one maintenance cycle and exits, for the scheduled job that replaces the
    // long-running worker's housekeeping while the worker is scaled to zero.
    public const string MaintenanceArgument = "--maintenance";

    public static async Task<int> Main(string[] args)
    {
        var runMaintenanceOnce = args.Contains(MaintenanceArgument);
        var builder = Host.CreateApplicationBuilder(
            args.Where(arg => arg != MaintenanceArgument).ToArray());
        builder.AddServiceDefaults();
        builder.AddSqlServerDbContext<TravelMemoryDbContext>("travelmemory");
        builder.AddAzureBlobServiceClient("blobs");
        builder.AddAzureQueueServiceClient("queues");
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<PhotoImageProcessor>();
        builder.Services.AddScoped<PhotoJobProcessor>();
        builder.Services.AddSingleton<PhotoMaintenance>();
        if (!runMaintenanceOnce)
        {
            builder.Services.AddHostedService<PhotoQueueWorker>();
        }

        using var host = builder.Build();
        if (!runMaintenanceOnce)
        {
            await host.RunAsync();
            return 0;
        }

        // Starting and stopping the host also starts and flushes telemetry for the run.
        await host.StartAsync();
        await host.Services.GetRequiredService<PhotoMaintenance>().RunAsync(CancellationToken.None);
        await host.StopAsync();
        return 0;
    }
}
