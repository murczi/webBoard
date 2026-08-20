namespace Webboard.Infrastructure.Configuration.Configurations;

using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class ModuleConfiguration : IEntityTypeConfiguration<ModuleEntity> {
    public void Configure(EntityTypeBuilder<ModuleEntity> builder) {
        builder.ToTable("Modules");

        builder.HasKey(keyExpression: module => module.Id);

        builder.HasOne(navigationExpression: module => module.Type)
               .WithMany(navigationExpression: type => type.Modules)
               .HasForeignKey(foreignKeyExpression: module => module.TypeId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(navigationExpression: module => module.Host)
               .WithMany(navigationExpression: host => host.Modules)
               .HasForeignKey(foreignKeyExpression: module => module.HostId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.Property(propertyExpression: module => module.FriendlyName)
               .IsRequired()
               .HasMaxLength(maxLength: 100);

        builder.Property(propertyExpression: module => module.Description)
               .HasMaxLength(maxLength: 1000);

        builder.Property(propertyExpression: module => module.HealthCheckUrl)
               .HasMaxLength(maxLength: 2048);

        builder.Property(propertyExpression: module => module.ManagementUrl)
               .HasMaxLength(maxLength: 2048);

        builder.Property(propertyExpression: module => module.DeletionFlag)
               .HasDefaultValue(value: false);

        builder.Property(propertyExpression: module => module.IsEnabled)
               .HasDefaultValue(value: true);

        builder.HasIndex(indexExpression: module => module.HostId);
    }
}
