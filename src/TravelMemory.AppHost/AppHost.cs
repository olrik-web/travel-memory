var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql")
    .WithImageTag("2025-latest")
    .WithDataVolume();
var database = sql.AddDatabase("travelmemory");

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator =>
    {
        emulator.WithDataVolume();
        emulator.WithArgs("--skipApiVersionCheck");
    });
var blobs = storage.AddBlobs("blobs");
var queues = storage.AddQueues("queues");

var api = builder.AddProject<Projects.TravelMemory_Api>("api")
    .WithReference(database)
    .WaitFor(database)
    .WithReference(blobs)
    .WaitFor(blobs)
    .WithReference(queues)
    .WaitFor(queues)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.TravelMemory_Worker>("worker")
    .WithReference(database)
    .WaitFor(database)
    .WithReference(blobs)
    .WaitFor(blobs)
    .WithReference(queues)
    .WaitFor(queues)
    .WaitFor(api);

var web = builder.AddViteApp("web", "../TravelMemory.Web")
    .WithReference(api)
    .WaitFor(api);

api.PublishWithContainerFiles(web, "wwwroot");

builder.Build().Run();
