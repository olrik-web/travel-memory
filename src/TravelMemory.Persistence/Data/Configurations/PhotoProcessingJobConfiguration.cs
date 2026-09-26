using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TravelMemory.Domain.Photos;

namespace TravelMemory.Persistence.Data.Configurations;

internal sealed class PhotoProcessingJobConfiguration : IEntityTypeConfiguration<PhotoProcessingJob>
{
    public void Configure(EntityTypeBuilder<PhotoProcessingJob> builder)
    {
        builder.ToTable("PhotoProcessingJobs");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(value => value.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(value => value.LastError).HasMaxLength(2000);
        builder.Property(value => value.AvailableAtUtc).HasPrecision(7);
        builder.Property(value => value.LastDispatchedAtUtc).HasPrecision(7);
        builder.Property(value => value.TraceParent)
            .HasMaxLength(PhotoProcessingJob.MaxTraceParentLength)
            .IsUnicode(false);
        builder.Property(value => value.CreatedAtUtc).HasPrecision(7);
        builder.Property(value => value.UpdatedAtUtc).HasPrecision(7);
        builder.Property(value => value.Version).IsRowVersion();
        builder.HasOne<PhotoImportItem>()
            .WithMany()
            .HasForeignKey(value => value.ImportItemId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(value => new { value.ImportItemId, value.Kind }).IsUnique();
        builder.HasIndex(value => new { value.State, value.AvailableAtUtc });
    }
}
