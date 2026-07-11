using Dashboard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dashboard.Infrastructure.Persistence.Configurations;

public sealed class ModuleStatusConfiguration
    : IEntityTypeConfiguration<ModuleStatus>
{
    public void Configure(EntityTypeBuilder<ModuleStatus> builder)
    {
        builder.ToTable("ModuleStatuses");

        builder.HasKey(status => status.DashboardModuleId);

        builder.Property(status => status.Health)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(status => status.Message)
            .HasMaxLength(2000);

        builder.HasOne(status => status.Module)
            .WithOne(module => module.CurrentStatus)
            .HasForeignKey<ModuleStatus>(
                status => status.DashboardModuleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}