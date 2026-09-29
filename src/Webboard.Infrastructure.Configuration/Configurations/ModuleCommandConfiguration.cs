namespace Webboard.Infrastructure.Configuration.Configurations;

using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class ModuleCommandConfiguration : IEntityTypeConfiguration<ModuleCommandEntity> {
    public void Configure(EntityTypeBuilder<ModuleCommandEntity> builder) {
        builder.ToTable("ModuleCommands");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CommandId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Label).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => new { x.ModuleId, x.HostId, x.CommandId }).IsUnique();
        builder.HasOne(x => x.Module).WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Host).WithMany().HasForeignKey(x => x.HostId).OnDelete(DeleteBehavior.Restrict);
    }
}
