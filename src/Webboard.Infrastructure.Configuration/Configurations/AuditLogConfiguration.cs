namespace Webboard.Infrastructure.Configuration.Configurations;

using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLogEntity> {
    public void Configure(EntityTypeBuilder<AuditLogEntity> builder) {
        builder.ToTable("AuditLogs");

        builder.HasKey(keyExpression: log => log.Id);

        builder.Property(propertyExpression: log => log.AuditComment)
               .IsRequired()
               .HasMaxLength(maxLength: 100);

        builder.HasIndex(indexExpression: log => log.DateCreated);

        builder.HasIndex(indexExpression: log => log.ModuleId);

        builder.HasOne(navigationExpression: log => log.Module)
               .WithMany(navigationExpression: module => module.AuditLogs)
               .HasForeignKey(foreignKeyExpression: log => log.ModuleId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}
