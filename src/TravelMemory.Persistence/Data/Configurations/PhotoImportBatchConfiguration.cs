using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TravelMemory.Domain.Photos;
using TravelMemory.Domain.Trips;

namespace TravelMemory.Persistence.Data.Configurations;

internal sealed class PhotoImportBatchConfiguration : IEntityTypeConfiguration<PhotoImportBatch>
{
    public void Configure(EntityTypeBuilder<PhotoImportBatch> builder)
    {
        builder.ToTable(
            "PhotoImportBatches",
            table => table.HasCheckConstraint(
                "CK_PhotoImportBatches_ExpectedFileCount",
                $"[ExpectedFileCount] BETWEEN 1 AND {PhotoImportBatch.MaximumFileCount}"));
        builder.HasKey(value => value.Id);
        builder.Property(value => value.State).HasConversion<string>().HasMaxLength(32);
        builder.Property(value => value.CreatedAtUtc).HasPrecision(7);
        builder.Property(value => value.UpdatedAtUtc).HasPrecision(7);
        builder.HasOne<Trip>()
            .WithMany()
            .HasForeignKey(value => value.TripId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.OwnerId, value.TripId, value.CreatedAtUtc })
            .IsDescending(false, false, true);
        builder.HasIndex(value => new { value.OwnerId, value.TripId, value.ClientBatchId })
            .IsUnique();
    }
}
