namespace Webboard.Infrastructure.Configuration;

using Entities;
using Microsoft.EntityFrameworkCore;

public class WebboardDbContext(DbContextOptions<WebboardDbContext> options) : DbContext(options) {
    public DbSet<ModuleTypeEntity> ModuleTypes => Set<ModuleTypeEntity>();
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();
    public DbSet<ModuleEntity> Modules => Set<ModuleEntity>();
    public DbSet<HostEntity> Hosts => Set<HostEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
        typeof(WebboardDbContext).Assembly);
    }
}
