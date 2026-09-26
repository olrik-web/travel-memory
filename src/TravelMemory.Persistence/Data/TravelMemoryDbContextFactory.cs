using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TravelMemory.Persistence.Data;

// Used only by the dotnet-ef tool, on Windows and Linux alike. Adding a migration needs a
// context but never a database, so a placeholder connection string is enough. Commands that
// do connect, such as database update, take the real one from the environment and explain
// what is missing instead of failing somewhere inside SqlClient.
internal sealed class TravelMemoryDbContextFactory
    : IDesignTimeDbContextFactory<TravelMemoryDbContext>
{
    public const string ConnectionStringVariable = "TRAVELMEMORY_DESIGN_TIME_CONNECTION";

    public TravelMemoryDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        var options = new DbContextOptionsBuilder<TravelMemoryDbContext>()
            .UseSqlServer(connectionString ?? "Server=design-time-placeholder;Database=travelmemory");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            options.AddInterceptors(new MissingConnectionInterceptor());
        }

        return new TravelMemoryDbContext(options.Options);
    }

    private sealed class MissingConnectionInterceptor : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result) =>
            throw CreateException();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            throw CreateException();

        private static InvalidOperationException CreateException() =>
            new($"This dotnet ef command needs a database. Set {ConnectionStringVariable} to a SQL Server "
                + "connection string, for example the travelmemory connection string from the Aspire "
                + "dashboard. Adding migrations does not need it.");
    }
}
