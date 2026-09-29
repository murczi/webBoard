namespace Webboard.Infrastructure.Configuration.Configurations;

using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Webboard.Domain.Model.AuditLogs;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLogEntity> {
    public void Configure(EntityTypeBuilder<AuditLogEntity> builder) {
        builder.ToTable("AuditLogs");
        builder.HasIndex(x => x.OperationId).IsUnique();
        builder.Property(x => x.Operation).HasMaxLength(100);
        builder.Property(x => x.Outcome).HasMaxLength(32);
        builder.Property(x => x.Failure).HasMaxLength(1000);
        builder.Property(x => x.Output).HasMaxLength(16384);
        builder.Property(x => x.TargetSnapshot).HasMaxLength(300);


        builder.Property(log => log.Action).HasDefaultValue(AuditAction.Legacy);
        builder.HasIndex(log => new { log.DateCreated, log.Id });
        builder.HasIndex(log => new { log.Action, log.DateCreated, log.Id });
        builder.HasIndex(log => new { log.ActorId, log.DateCreated, log.Id });
        builder.HasIndex(log => new { log.ModuleId, log.DateCreated, log.Id });
        builder.HasIndex(log => new { log.HostId, log.DateCreated, log.Id });
        builder.HasIndex(log => new { log.UserId, log.DateCreated, log.Id });

        builder.HasKey(keyExpression: log => log.Id);

        builder.HasOne(navigationExpression: log => log.Actor)
               .WithMany(navigationExpression: user => user.AuditLogsAsActor)
               .HasForeignKey(foreignKeyExpression: log => log.ActorId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.Property(propertyExpression: log => log.Comment)
               .IsRequired()
               .HasMaxLength(maxLength: AuditLogModel.MaxCommentLength);

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
