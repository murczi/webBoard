namespace Webboard.Infrastructure.Configuration.Configurations;

using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLogEntity> {
    public void Configure(EntityTypeBuilder<AuditLogEntity> builder) {
        builder.ToTable("AuditLogs");

        builder.HasKey(keyExpression: log => log.Id);

        builder.HasOne(navigationExpression: log => log.Actor)
               .WithMany(navigationExpression: user => user.AuditLogsAsActor)
               .HasForeignKey(foreignKeyExpression: log => log.ActorId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.Property(propertyExpression: log => log.Comment)
               .IsRequired()
               .HasMaxLength(maxLength: 100);

        builder.HasOne(navigationExpression: log => log.Module)
               .WithMany(navigationExpression: module => module.AuditLogs)
               .HasForeignKey(foreignKeyExpression: log => log.ModuleId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(navigationExpression: log => log.Host)
               .WithMany(navigationExpression: host => host.AuditLogs)
               .HasForeignKey(foreignKeyExpression: log => log.HostId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(navigationExpression: log => log.User)
               .WithMany(navigationExpression: user => user.AuditLogsAsTarget)
               .HasForeignKey(foreignKeyExpression: log => log.UserId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(indexExpression: log => log.DateCreated);

    }
}
