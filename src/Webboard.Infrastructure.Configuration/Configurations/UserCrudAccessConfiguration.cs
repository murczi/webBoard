namespace Webboard.Infrastructure.Configuration.Configurations;

using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class UserCrudAccessConfiguration
    : IEntityTypeConfiguration<UserCrudAccessEntity> {
    public void Configure(EntityTypeBuilder<UserCrudAccessEntity> builder) {
        builder.ToTable("UserCrudAccess", tableBuilder => {
            tableBuilder.HasCheckConstraint(
                "CK_UserCrudAccess_Resource",
                "\"Resource\" IN ('Users', 'Hosts', 'Modules', 'ModuleTypes', 'AuditLogs')");

            tableBuilder.HasCheckConstraint(
                "CK_UserCrudAccess_ReadOnlyResources",
                "\"Resource\" NOT IN ('ModuleTypes', 'AuditLogs') OR " +
                "(NOT \"CanCreate\" AND NOT \"CanUpdate\" AND NOT \"CanDelete\")");
        });

        builder.HasKey(access => access.Id);

        builder.Property(access => access.Resource)
               .IsRequired()
               .HasMaxLength(64);

        builder.HasIndex(access => new { access.UserId, access.Resource })
               .IsUnique();

        builder.HasOne(access => access.User)
               .WithMany(user => user.CrudAccess)
               .HasForeignKey(access => access.UserId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
