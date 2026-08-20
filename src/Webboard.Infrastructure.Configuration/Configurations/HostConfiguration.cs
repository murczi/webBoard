namespace Webboard.Infrastructure.Configuration.Configurations;

using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class HostConfiguration : IEntityTypeConfiguration<HostEntity> {
    public void Configure(EntityTypeBuilder<HostEntity> builder) {
        builder.ToTable("Hosts");

        builder.HasKey(keyExpression: host => host.Id);

        builder.Property(propertyExpression: host => host.Name)
               .IsRequired()
               .HasMaxLength(maxLength: 100);

        builder.Property(propertyExpression: host => host.AgentBaseUrl)
               .IsRequired()
               .HasMaxLength(maxLength: 2048);

        builder.HasIndex(indexExpression: host => host.Name)
               .IsUnique();

        builder.HasMany(navigationExpression: host => host.Modules)
               .WithOne(navigationExpression: module => module.Host)
               .HasForeignKey(foreignKeyExpression: module => module.HostId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
