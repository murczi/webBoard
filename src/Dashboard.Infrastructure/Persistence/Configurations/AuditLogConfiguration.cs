using Dashboard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dashboard.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration
    : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(log => log.Id);

        builder.Property(log => log.ActorDisplayName)
            .HasMaxLength(200);

        builder.Property(log => log.Action)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(log => log.Details)
            .HasColumnType("text");

        builder.HasIndex(log => log.OccurredAtUtc);

        builder.HasIndex(log => log.DashboardModuleId);

        builder.HasOne(log => log.Module)
            .WithMany(module => module.AuditLogs)
            .HasForeignKey(log => log.DashboardModuleId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}