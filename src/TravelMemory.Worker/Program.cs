using TravelMemory.Persistence.Data;

namespace TravelMemory.Worker;

internal static class WorkerProgram
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.AddServiceDefaults();
        builder.AddSqlServerDbContext<TravelMemoryDbContext>("travelmemory");
        builder.AddAzureBlobServiceClient("blobs");
        builder.AddAzureQueueServiceClient("queues");
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<PhotoImageProcessor>();
        builder.Services.AddScoped<PhotoJobProcessor>();
        builder.Services.AddHostedService<PhotoQueueWorker>();

        var host = builder.Build();
        host.Run();
    }
}
