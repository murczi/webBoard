namespace Webboard.Infrastructure.Configuration.Configurations;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
public sealed class MonitoringResultConfiguration : IEntityTypeConfiguration<MonitoringResultEntity> {
    public void Configure(EntityTypeBuilder<MonitoringResultEntity> builder) {
        builder.ToTable("MonitoringResults"); builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.ModuleId, x.Timestamp, x.Id });
        builder.HasIndex(x => x.Timestamp);
        builder.Property(x => x.Message).HasMaxLength(1000);
        builder.HasOne<ModuleEntity>().WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Cascade);
    }
}
public sealed class LatestMonitoringConfiguration : IEntityTypeConfiguration<LatestMonitoringEntity> {
    public void Configure(EntityTypeBuilder<LatestMonitoringEntity> builder) {
        builder.ToTable("LatestMonitoring"); builder.HasKey(x => x.ModuleId);
        builder.Property(x => x.Message).HasMaxLength(1000);
        builder.Property(x => x.Details).HasColumnType("jsonb");
        builder.HasOne<ModuleEntity>().WithOne().HasForeignKey<LatestMonitoringEntity>(x => x.ModuleId).OnDelete(DeleteBehavior.Cascade);
    }
}
