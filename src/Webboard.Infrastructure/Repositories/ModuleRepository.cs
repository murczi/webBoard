namespace Webboard.Infrastructure.Repositories;

using Configuration;
using Configuration.Entities;
using Domain.Interfaces.Repositories;
using Domain.Model.Modules;
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
        module.Id = entity.Id;
    }

    public async Task<bool> UpdateAsync(
        ModuleModel module,
        int actorId,
        string auditComment,
        CancellationToken cancellationToken = default) {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await MonitoringInvalidation.LockAsync(dbContext, [module.Id], cancellationToken);
        var updated = await dbContext.Modules
            .Where(entity => entity.Id == module.Id && !entity.DeletionFlag)
            .ExecuteUpdateAsync(
                setters => setters
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
        if (!updated)
            return false;

        await MonitoringInvalidation.InvalidateAsync(dbContext, [module.Id], timeProvider, cancellationToken);
        AddAuditLog(module.Id, actorId, auditComment, Webboard.Domain.Model.AuditLogs.AuditAction.Update);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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
