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

// Fixed ports: the issuer URL is part of every user's identity mapping, and the realm lists
// the web app's callback URL as an allowed redirect.
const int KeycloakPort = 8180;
const int WebPort = 5173;
var oidcClientSecret = builder.AddParameter(
    "oidc-client-secret",
    new GenerateParameterDefault { MinLength = 32, Special = false },
    secret: true,
    persist: true);
// The realm file reads the client secret from this variable, so none is committed.
// Plain HTTP locally: browsers do not always trust the development certificate, and a
// warning page in the middle of the sign-in redirect is easy to mistake for a bug.
#pragma warning disable ASPIRECERTIFICATES001 // Evaluation API; Keycloak hosting is in preview too.
var keycloak = builder.AddKeycloak("keycloak", KeycloakPort)
    .WithoutHttpsCertificate()
    .WithRealmImport("./Realms")
    .WithEnvironment("TRAVEL_MEMORY_WEB_CLIENT_SECRET", oidcClientSecret);
#pragma warning restore ASPIRECERTIFICATES001

var api = builder.AddProject<Projects.TravelMemory_Api>("api")
    .WithReference(database)
    .WaitFor(database)
    .WithReference(blobs)
    .WaitFor(blobs)
    .WithReference(queues)
    .WaitFor(queues)
    .WaitFor(keycloak)
    .WithEnvironment(
        "Authentication__Oidc__Authority",
        ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/travel-memory"))
    .WithEnvironment("Authentication__Oidc__ClientId", "travel-memory-web")
    .WithEnvironment("Authentication__Oidc__ClientSecret", oidcClientSecret)
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
    .WithEndpoint("http", endpoint => endpoint.Port = WebPort)
    .WithReference(api)
    .WaitFor(api);

api.PublishWithContainerFiles(web, "wwwroot");

builder.Build().Run();
