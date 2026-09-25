using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TravelMemory.Domain.Users;

namespace TravelMemory.Persistence.Data.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Issuer).HasMaxLength(User.MaxIssuerLength).IsRequired();
        builder.Property(value => value.Subject).HasMaxLength(User.MaxSubjectLength).IsRequired();
        builder.Property(value => value.CreatedAtUtc).HasPrecision(7).IsRequired();
        builder.HasIndex(value => new { value.Issuer, value.Subject }).IsUnique();
    }
}
