using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TravelMemory.Domain.Trips;

namespace TravelMemory.Persistence.Data.Configurations;

internal sealed class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> builder)
    {
        builder.ToTable(
            "Trips",
            table => table.HasCheckConstraint(
                "CK_Trips_DateRange",
                "[StartDate] IS NULL OR [EndDate] IS NULL OR [EndDate] >= [StartDate]"));
        builder.HasKey(value => value.Id);
        builder.Property(value => value.OwnerId).IsRequired();
        builder.Property(value => value.Title).HasMaxLength(Trip.MaxTitleLength).IsRequired();
        builder.Property(value => value.StartDate).HasColumnType("date");
        builder.Property(value => value.EndDate).HasColumnType("date");
        builder.Property(value => value.CreatedAtUtc).HasPrecision(7).IsRequired();
        builder.Property(value => value.DeletingSinceUtc).HasPrecision(7);
        // A trip being deleted is gone for every query; only the deletion itself looks
        // past this filter to resume.
        builder.HasQueryFilter(value => value.DeletingSinceUtc == null);
        builder.HasIndex(value => new { value.OwnerId, value.StartDate, value.CreatedAtUtc })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_Trips_OwnerId_StartDate_CreatedAtUtc");
    }
}
