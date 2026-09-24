using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TravelMemory.Domain.Photos;
using TravelMemory.Domain.Trips;

namespace TravelMemory.Persistence.Data.Configurations;

internal sealed class PhotoConfiguration : IEntityTypeConfiguration<Photo>
{
    public void Configure(EntityTypeBuilder<Photo> builder)
    {
        builder.ToTable("Photos");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.ContentHash).HasMaxLength(64).IsUnicode(false);
        builder.Property(value => value.OriginalFileName).HasMaxLength(255);
        builder.Property(value => value.CapturedAtOriginalLocal).HasColumnType("datetime2(0)");
        builder.Property(value => value.CapturedAtTimelineLocal).HasColumnType("datetime2(0)");
        builder.Property(value => value.WebBlobName).HasMaxLength(512).IsUnicode(false);
        builder.Property(value => value.ThumbnailBlobName).HasMaxLength(512).IsUnicode(false);
        builder.Property(value => value.CreatedAtUtc).HasPrecision(7);
        builder.HasOne<Trip>()
            .WithMany()
            .HasForeignKey(value => value.TripId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PhotoImportBatch>()
            .WithMany()
            .HasForeignKey(value => value.ImportBatchId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PhotoImportItem>()
            .WithOne()
            .HasForeignKey<Photo>(value => value.ImportItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.OwnerId, value.TripId, value.ContentHash })
            .IsUnique();
        builder.HasIndex(value => value.ImportItemId).IsUnique();
        builder.HasIndex(value => new
            {
                value.OwnerId,
                value.TripId,
                value.CapturedAtTimelineLocal,
                value.Id,
            });
    }
}
