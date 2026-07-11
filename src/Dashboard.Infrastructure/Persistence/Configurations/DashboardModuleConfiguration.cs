using Dashboard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dashboard.Infrastructure.Persistence.Configurations;

public sealed class DashboardModuleConfiguration
    : IEntityTypeConfiguration<DashboardModule>
{
    public void Configure(EntityTypeBuilder<DashboardModule> builder)
    {
        builder.ToTable("DashboardModules");

        builder.HasKey(module => module.Id);

        builder.Property(module => module.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(module => module.Description)
            .HasMaxLength(1000);

        builder.Property(module => module.Type)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(module => module.TargetIdentifier)
            .HasMaxLength(255);

        builder.Property(module => module.HealthCheckUrl)
            .HasMaxLength(2048);

        builder.Property(module => module.ManagementUrl)
            .HasMaxLength(2048);

        builder.HasIndex(module => new
        {
            module.ManagedHostId,
            module.SortOrder
        });
    }
}