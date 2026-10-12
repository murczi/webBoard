namespace Webboard.Infrastructure.Repositories;

using Configuration;
using Configuration.Entities;
using Domain.Interfaces.Repositories;
using Domain.Model.Modules;
using Domain.Services;
using Microsoft.EntityFrameworkCore;

public class ModuleRepository(WebboardDbContext dbContext) : IModuleRepository {
    private readonly TimeProvider timeProvider = TimeProvider.System;
    public ModuleRepository(WebboardDbContext dbContext, TimeProvider timeProvider) : this(dbContext) => this.timeProvider = timeProvider;

    public async Task<IReadOnlyList<ModuleModel>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Modules.AsNoTracking()
            .Where(module => !module.DeletionFlag)
            .OrderBy(module => module.FriendlyName)
            .Select(ModuleProjection.Read)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ModuleOptionModel>> GetHostsAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Hosts.AsNoTracking()
            .Where(host => !host.DeletionFlag && host.IsEnabled)
            .OrderBy(host => host.Name)
            .Select(host => new ModuleOptionModel(host.Id, host.Name))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ModuleOptionModel>> GetTypesAsync(CancellationToken cancellationToken = default) =>
        await dbContext.ModuleTypes.AsNoTracking()
            .OrderBy(type => type.Name)
            .Select(type => new ModuleOptionModel(type.Id, type.Name))
            .ToListAsync(cancellationToken);

    public Task<bool> HostExistsAsync(int hostId, CancellationToken cancellationToken = default) =>
        dbContext.Hosts.AnyAsync(host => host.Id == hostId && !host.DeletionFlag, cancellationToken);

    public Task<string?> GetTypeNameAsync(int typeId, CancellationToken cancellationToken = default) =>
        dbContext.ModuleTypes.AsNoTracking()
            .Where(type => type.Id == typeId)
            .Select(type => type.Name)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task AddAsync(
        ModuleModel module,
        int actorId,
        string auditComment,
        CancellationToken cancellationToken = default) {
        var entity = ModuleProjection.ToEntity(module);
        dbContext.Modules.Add(entity);
        dbContext.AuditLogs.Add(new AuditLogEntity
        {
            ActorId = actorId,
            Action = Webboard.Domain.Model.AuditLogs.AuditAction.Create,
            Comment = auditComment,
            DateCreated = timeProvider.GetUtcNow(),
            Module = entity,
            Actor = null!
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        module.Id = entity.Id; module.Version = entity.Version;
    }

    public async Task<bool> UpdateAsync(
        ModuleModel module,
        int actorId,
        string auditComment,
        CancellationToken cancellationToken = default) {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await MonitoringInvalidation.LockAsync(dbContext, [module.Id], cancellationToken);
        var current = await dbContext.Modules.AsNoTracking().SingleOrDefaultAsync(x => x.Id == module.Id && !x.DeletionFlag, cancellationToken);
        if (current is null) return false;
        var monitoringChanged = MonitoringConfigurationFingerprint.From(ModuleProjection.ToModel(current)) != MonitoringConfigurationFingerprint.From(module);
        var monitoringVersion = current.MonitoringVersion + (monitoringChanged ? 1 : 0);
        var updated = await dbContext.Modules
            .Where(entity => entity.Id == module.Id && entity.Version == module.Version && !entity.DeletionFlag)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(entity => entity.Version, entity => entity.Version + 1)
                    .SetProperty(entity => entity.MonitoringVersion, monitoringVersion)
                    .SetProperty(entity => entity.TypeId, module.TypeId)
                    .SetProperty(entity => entity.HostId, module.HostId)
                    .SetProperty(entity => entity.FriendlyName, module.Name)
                    .SetProperty(entity => entity.Description, module.Description)
                    .SetProperty(entity => entity.HealthCheckUrl, module.HealthCheckUrl)
                    .SetProperty(entity => entity.ContainerId, module.ContainerId)
                    .SetProperty(entity => entity.ServiceName, module.ServiceName)
                    .SetProperty(entity => entity.MinecraftServerAddress, module.MinecraftServerAddress)
                    .SetProperty(entity => entity.MinecraftServerPort, module.MinecraftServerPort)
                    .SetProperty(entity => entity.SteamServerAddress, module.SteamServerAddress)
                    .SetProperty(entity => entity.SteamQueryPort, module.SteamQueryPort)
                    .SetProperty(entity => entity.SteamQueryPlayers, module.SteamQueryPlayers)
                    .SetProperty(entity => entity.ManagementUrl, module.ManagementUrl)
                    .SetProperty(entity => entity.IsEnabled, module.IsEnabled)
                    .SetProperty(entity => entity.DateUpdated, module.DateUpdated),
                cancellationToken) > 0;
        if (!updated) {
            if (await dbContext.Modules.AnyAsync(x => x.Id == module.Id && !x.DeletionFlag, cancellationToken)) throw new ResourceConflictException();
            return false;
        }

        if (monitoringChanged) await MonitoringInvalidation.InvalidateAsync(dbContext, [module.Id], timeProvider, cancellationToken);
        AddAuditLog(module.Id, actorId, auditComment, Webboard.Domain.Model.AuditLogs.AuditAction.Update);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        module.Version++;
        return true;
    }

    public async Task<bool> DeleteAsync(
        int moduleId,
        int actorId,
        string auditComment,
        CancellationToken cancellationToken = default) {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await MonitoringInvalidation.LockAsync(dbContext, [moduleId], cancellationToken);
        var deleted = await dbContext.Modules
            .Where(module => module.Id == moduleId && !module.DeletionFlag)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(module => module.Version, module => module.Version + 1)
                    .SetProperty(module => module.MonitoringVersion, module => module.MonitoringVersion + 1)
                    .SetProperty(module => module.DeletionFlag, true)
                    .SetProperty(module => module.IsEnabled, false)
                    .SetProperty(module => module.DateUpdated, timeProvider.GetUtcNow()),
                cancellationToken) > 0;
        if (!deleted)
            return false;

        await MonitoringInvalidation.InvalidateAsync(dbContext, [moduleId], timeProvider, cancellationToken);
        AddAuditLog(moduleId, actorId, auditComment, Webboard.Domain.Model.AuditLogs.AuditAction.Delete);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private void AddAuditLog(int moduleId, int actorId, string comment, Webboard.Domain.Model.AuditLogs.AuditAction action) =>
        dbContext.AuditLogs.Add(new AuditLogEntity
        {
            ActorId = actorId,
            Action = action,
            Comment = comment,
            DateCreated = timeProvider.GetUtcNow(),
            ModuleId = moduleId,
            Actor = null!
        });
}
