using Dashboard.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dashboard.Infrastructure.Persistence;

public sealed class DashboardDbContext(
    DbContextOptions<DashboardDbContext> options)
    : DbContext(options)
{
    public DbSet<ManagedHost> ManagedHosts => Set<ManagedHost>();

    public DbSet<DashboardModule> DashboardModules
        => Set<DashboardModule>();

    public DbSet<ModuleStatus> ModuleStatuses
        => Set<ModuleStatus>();

    public DbSet<AuditLog> AuditLogs
        => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(DashboardDbContext).Assembly);
    }
}