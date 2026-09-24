using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TravelMemory.Persistence.Data;

internal sealed class TravelMemoryDbContextFactory
    : IDesignTimeDbContextFactory<TravelMemoryDbContext>
{
    public TravelMemoryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TravelMemoryDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Database=TravelMemoryDesignTime;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        return new TravelMemoryDbContext(options);
    }
}
