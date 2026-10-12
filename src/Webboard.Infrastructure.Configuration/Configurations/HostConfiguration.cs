namespace Webboard.Infrastructure.Configuration.Configurations;

using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class HostConfiguration : IEntityTypeConfiguration<HostEntity> {
    public void Configure(EntityTypeBuilder<HostEntity> builder) {
        builder.Property(host => host.Version).HasDefaultValue(0L).IsConcurrencyToken();
        builder.ToTable("Hosts");

        builder.HasKey(keyExpression: host => host.Id);

        builder.Property(propertyExpression: host => host.Name)
               .IsRequired()
               .HasMaxLength(maxLength: 100);

        builder.Property(propertyExpression: host => host.AgentBaseUrl)
               .IsRequired()
               .HasMaxLength(maxLength: 2048);

        builder.Property(host => host.NormalizedName).HasComputedColumnSql("lower(\"Name\")", stored: true).HasMaxLength(100);
        builder.HasIndex(indexExpression: host => host.NormalizedName)
               .IsUnique();

        builder.HasMany(navigationExpression: host => host.Modules)
               .WithOne(navigationExpression: module => module.Host)
               .HasForeignKey(foreignKeyExpression: module => module.HostId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
