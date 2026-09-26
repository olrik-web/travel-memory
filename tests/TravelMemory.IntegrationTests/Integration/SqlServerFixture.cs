using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace TravelMemory.IntegrationTests.Integration;

// One SQL Server container is shared by all integration tests, and each test gets its own
// database. SQL Server 2025 occasionally crashes during container startup on CI runners,
// so starting it once instead of once per test keeps that crash rare, and a startup crash
// is retried with a fresh container.
public sealed class SqlServerFixture : IAsyncLifetime
{
    private const int MaxStartAttempts = 3;

    private MsSqlContainer? container;

    public async ValueTask InitializeAsync()
    {
        for (var attempt = 1; ; attempt++)
        {
            var candidate = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest")
                .Build();
            try
            {
                await candidate.StartAsync();
                container = candidate;
                return;
            }
            catch (ContainerNotRunningException) when (attempt < MaxStartAttempts)
            {
                await candidate.DisposeAsync();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    public string CreateDatabaseConnectionString()
    {
        var builder = new SqlConnectionStringBuilder(
            (container ?? throw new InvalidOperationException("SQL Server is not started."))
                .GetConnectionString())
        {
            InitialCatalog = $"travelmemory_{Guid.NewGuid():N}",
        };
        return builder.ConnectionString;
    }
}
