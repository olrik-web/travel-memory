using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TravelMemory.Domain.Photos;

namespace TravelMemory.Persistence.Data.Configurations;

internal sealed class PhotoImportItemConfiguration : IEntityTypeConfiguration<PhotoImportItem>
{
    public void Configure(EntityTypeBuilder<PhotoImportItem> builder)
    {
        builder.ToTable(
            "PhotoImportItems",
            table => table.HasCheckConstraint(
                "CK_PhotoImportItems_ExpectedSizeBytes",
                "[ExpectedSizeBytes] > 0"));
        builder.HasKey(value => value.Id);
        builder.Property(value => value.ClientFileId).HasMaxLength(64).IsUnicode(false);
        builder.Property(value => value.OriginalFileName).HasMaxLength(255);
        builder.Property(value => value.ContentType).HasMaxLength(64).IsUnicode(false);
        builder.Property(value => value.TemporaryBlobName).HasMaxLength(512).IsUnicode(false);
        builder.Property(value => value.State).HasConversion<string>().HasMaxLength(32);
        builder.Property(value => value.Outcome).HasConversion<string>().HasMaxLength(16);
        builder.Property(value => value.ContentHash).HasMaxLength(64).IsUnicode(false);
        builder.Property(value => value.CapturedAtOriginalLocal).HasColumnType("datetime2(0)");
        builder.Property(value => value.ErrorCode).HasMaxLength(64).IsUnicode(false);
        builder.Property(value => value.ErrorMessage).HasMaxLength(1000);
        builder.Property(value => value.DerivativesVerifiedAtUtc).HasPrecision(7);
        builder.Property(value => value.OriginalDeletedAtUtc).HasPrecision(7);
        builder.Property(value => value.OriginalRetainedUntilUtc).HasPrecision(7);
        builder.Property(value => value.CreatedAtUtc).HasPrecision(7);
        builder.Property(value => value.UpdatedAtUtc).HasPrecision(7);
        builder.Property(value => value.Version).IsRowVersion();
        builder.HasOne<PhotoImportBatch>()
            .WithMany()
            .HasForeignKey(value => value.ImportBatchId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(value => new { value.ImportBatchId, value.ClientFileId }).IsUnique();
        builder.HasIndex(value => new { value.OwnerId, value.TripId, value.ContentHash });
    }
}
