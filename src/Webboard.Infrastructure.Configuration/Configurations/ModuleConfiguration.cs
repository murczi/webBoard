namespace Webboard.Infrastructure.Configuration.Configurations;

using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class ModuleConfiguration : IEntityTypeConfiguration<ModuleEntity> {
    public void Configure(EntityTypeBuilder<ModuleEntity> builder) {
        builder.ToTable("Modules");

        builder.HasKey(keyExpression: module => module.Id);

        builder.Property(propertyExpression: module => module.Name)
               .IsRequired()
               .HasMaxLength(maxLength: 100);

        builder.Property(propertyExpression: module => module.Description)
               .HasMaxLength(maxLength: 1000);

        builder.HasOne(navigationExpression: module => module.Type)
               .WithMany()
               .HasForeignKey(foreignKeyExpression: module => module.ModuleTypeId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.Property(propertyExpression: module => module.TargetIdentifier)
               .HasMaxLength(maxLength: 255);

        builder.Property(propertyExpression: module => module.HealthCheckUrl)
               .HasMaxLength(maxLength: 2048);

        builder.Property(propertyExpression: module => module.ManagementUrl)
               .HasMaxLength(maxLength: 2048);

        builder.Property(propertyExpression: module => module.ShowOnOverview)
               .HasDefaultValue(value: true);

        builder.HasIndex(indexExpression: module => new
        {
            module.ManagedHostId,
            module.SortOrder
        });
    }
}
