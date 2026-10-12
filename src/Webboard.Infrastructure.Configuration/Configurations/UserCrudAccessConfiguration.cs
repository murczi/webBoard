namespace Webboard.Infrastructure.Configuration.Configurations;

using Entities;
using Webboard.Domain.Model.UserCrudAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class UserCrudAccessConfiguration
    : IEntityTypeConfiguration<UserCrudAccessEntity> {
    public void Configure(EntityTypeBuilder<UserCrudAccessEntity> builder) {
        builder.ToTable("UserCrudAccess", tableBuilder => {
            tableBuilder.HasCheckConstraint(
                "CK_UserCrudAccess_Resource",
                "\"Resource\" IN (" + string.Join(", ", PermissionResources.All.Select(x => $"'{x.Key}'")) + ")");

            tableBuilder.HasCheckConstraint("CK_UserCrudAccess_Operations", "\"Resource\" = 'Modules' OR NOT \"CanOperate\"");
            tableBuilder.HasCheckConstraint(
                "CK_UserCrudAccess_ReadOnlyResources",
                "\"Resource\" NOT IN (" + string.Join(", ", PermissionResources.All.Where(x => x.IsReadOnly).Select(x => $"'{x.Key}'")) + ") OR " +
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
