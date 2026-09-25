using Microsoft.EntityFrameworkCore;
using TravelMemory.Domain.Photos;
using TravelMemory.Domain.Trips;
using TravelMemory.Domain.Users;

namespace TravelMemory.Persistence.Data;

public sealed class TravelMemoryDbContext(DbContextOptions<TravelMemoryDbContext> options)
    : DbContext(options)
{
    public DbSet<Trip> Trips => Set<Trip>();

    public DbSet<PhotoImportBatch> PhotoImportBatches => Set<PhotoImportBatch>();

    public DbSet<PhotoImportItem> PhotoImportItems => Set<PhotoImportItem>();

    public DbSet<PhotoProcessingJob> PhotoProcessingJobs => Set<PhotoProcessingJob>();

    public DbSet<Photo> Photos => Set<Photo>();

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TravelMemoryDbContext).Assembly);
    }
}
