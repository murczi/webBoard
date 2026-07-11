using Dashboard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dashboard.Infrastructure.Persistence.Configurations;

public sealed class ManagedHostConfiguration
    : IEntityTypeConfiguration<ManagedHost>
{
    public void Configure(EntityTypeBuilder<ManagedHost> builder)
    {
        builder.ToTable("ManagedHosts");

        builder.HasKey(host => host.Id);

        builder.Property(host => host.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(host => host.AgentBaseUrl)
            .IsRequired()
            .HasMaxLength(2048);

        builder.HasIndex(host => host.Name)
            .IsUnique();

        builder.HasMany(host => host.Modules)
            .WithOne(module => module.ManagedHost)
            .HasForeignKey(module => module.ManagedHostId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}