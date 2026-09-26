using Microsoft.EntityFrameworkCore;
using TravelMemory.Api;
using TravelMemory.Persistence.Data;

namespace TravelMemory.IntegrationTests.Integration;

[Collection(ContainerTestCollection.Name)]
public sealed class DatabaseMigrationTests(SqlServerFixture sqlServer)
{
    [Fact]
    public async Task Migrate_mode_applies_all_migrations_to_an_empty_database_and_exits()
    {
        var connectionString = sqlServer.CreateDatabaseConnectionString();

        var exitCode = await DatabaseMigration.RunAsync(
            [DatabaseMigration.Argument, $"--ConnectionStrings:travelmemory={connectionString}"])
            .WaitAsync(TimeSpan.FromMinutes(2));

        Assert.Equal(0, exitCode);
        await using var context = new TravelMemoryDbContext(
            new DbContextOptionsBuilder<TravelMemoryDbContext>()
                .UseSqlServer(connectionString)
                .Options);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync());
    }
}
