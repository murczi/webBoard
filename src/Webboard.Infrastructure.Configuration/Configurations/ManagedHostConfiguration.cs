namespace Webboard.Infrastructure.Configuration.Configurations;

using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class ManagedHostConfiguration : IEntityTypeConfiguration<ManagedHostEntity> {
    public void Configure(EntityTypeBuilder<ManagedHostEntity> builder) {
        builder.ToTable("ManagedHosts");

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
               .WithOne(navigationExpression: module => module.ManagedHost)
               .HasForeignKey(foreignKeyExpression: module => module.ManagedHostId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
