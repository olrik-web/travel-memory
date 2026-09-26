using System.Text.Json;
using System.Text.Json.Nodes;
using Testcontainers.Azurite;

namespace TravelMemory.IntegrationTests.Integration;

// The web app's TypeScript types are generated from src/TravelMemory.Web/openapi.json, so
// this test keeps that committed document in sync with what the API actually serves.
// To update it after an API change, run:
//   UPDATE_OPENAPI=1 dotnet test --project tests/TravelMemory.IntegrationTests --filter-class '*OpenApiDocumentTests'
[Collection(ContainerTestCollection.Name)]
public sealed class OpenApiDocumentTests(SqlServerFixture sqlServer, ITestOutputHelper output)
    : IAsyncLifetime
{
    private static readonly JsonSerializerOptions IndentedJson =
        new() { WriteIndented = true, NewLine = "\n" };

    private readonly string databaseConnectionString =
        sqlServer.CreateDatabaseConnectionString();
    private readonly AzuriteContainer azurite =
        new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.35.0")
            .WithCommand("--skipApiVersionCheck")
            .Build();

    public async ValueTask InitializeAsync() => await azurite.StartAsync();

    public ValueTask DisposeAsync() => azurite.DisposeAsync();

    [Fact]
    public async Task Committed_document_matches_the_api()
    {
        using var factory = new TravelMemoryApplicationFactory(
            databaseConnectionString,
            azurite.GetConnectionString(),
            Guid.NewGuid());
        using var client = factory.CreateClient();

        var actual = Normalize(await client.GetStringAsync("/openapi/v1.json"));
        var path = Path.Combine(FindRepositoryRoot(), "src", "TravelMemory.Web", "openapi.json");

        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI") == "1")
        {
            await File.WriteAllTextAsync(path, actual);
            return;
        }

        var expected = File.Exists(path) ? Normalize(await File.ReadAllTextAsync(path)) : "";
        if (expected != actual)
        {
            output.WriteLine(actual);
        }

        Assert.True(
            expected == actual,
            "src/TravelMemory.Web/openapi.json is out of date. Run "
            + "`UPDATE_OPENAPI=1 dotnet test --project tests/TravelMemory.IntegrationTests --filter-class '*OpenApiDocumentTests'`, "
            + "then `npm --prefix src/TravelMemory.Web run generate:api`.");
    }

    // Re-serializes with fixed formatting and drops the server list, which depends on the
    // host that served the document rather than on the API itself.
    private static string Normalize(string json)
    {
        var document = JsonNode.Parse(json)!.AsObject();
        document.Remove("servers");
        return document.ToJsonString(IndentedJson) + "\n";
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TravelMemory.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not find the repository root.");
    }
}
