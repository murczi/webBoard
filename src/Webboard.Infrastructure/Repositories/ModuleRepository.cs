namespace Webboard.Infrastructure.Repositories;

using Configuration;
using Configuration.Entities;
using Domain.Interfaces.Repositories;
using Domain.Model.Modules;
using Microsoft.EntityFrameworkCore;

public class ModuleRepository(WebboardDbContext dbContext) : IModuleRepository {
    public async Task<IReadOnlyList<ModuleModel>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Modules.AsNoTracking()
            .Where(module => !module.DeletionFlag)
            .OrderBy(module => module.FriendlyName)
            .Select(module => new ModuleModel
            {
                Id = module.Id,
                TypeId = module.TypeId,
                TypeName = module.Type != null ? module.Type.Name : string.Empty,
                HostId = module.HostId,
                HostName = module.Host != null ? module.Host.Name : null,
                HostAgentBaseUrl = module.Host != null ? module.Host.AgentBaseUrl : null,
                Name = module.FriendlyName,
                Description = module.Description,
                HealthCheckUrl = module.HealthCheckUrl,
                ContainerId = module.ContainerId,
                ServiceName = module.ServiceName,
                ManagementUrl = module.ManagementUrl,
                IsEnabled = module.IsEnabled,
                DateCreated = module.DateCreated,
                DateUpdated = module.DateUpdated
            })
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ModuleOptionModel>> GetHostsAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Hosts.AsNoTracking()
            .Where(host => !host.DeletionFlag)
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
        var entity = new ModuleEntity
        {
            TypeId = module.TypeId,
            HostId = module.HostId,
            FriendlyName = module.Name,
            Description = module.Description,
            HealthCheckUrl = module.HealthCheckUrl,
            ContainerId = module.ContainerId,
            ServiceName = module.ServiceName,
            ManagementUrl = module.ManagementUrl,
            DeletionFlag = false,
            IsEnabled = module.IsEnabled,
            DateCreated = module.DateCreated,
            DateUpdated = module.DateUpdated
        };
        dbContext.Modules.Add(entity);
        dbContext.AuditLogs.Add(new AuditLogEntity
        {
            ActorId = actorId,
            Comment = auditComment,
            DateCreated = DateTime.UtcNow,
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
                    .SetProperty(entity => entity.ManagementUrl, module.ManagementUrl)
                    .SetProperty(entity => entity.IsEnabled, module.IsEnabled)
                    .SetProperty(entity => entity.DateUpdated, module.DateUpdated),
                cancellationToken) > 0;
        if (!updated)
            return false;

        AddAuditLog(module.Id, actorId, auditComment);
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
        var deleted = await dbContext.Modules
            .Where(module => module.Id == moduleId && !module.DeletionFlag)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(module => module.DeletionFlag, true)
                    .SetProperty(module => module.IsEnabled, false)
                    .SetProperty(module => module.DateUpdated, DateTimeOffset.UtcNow),
                cancellationToken) > 0;
        if (!deleted)
            return false;

        AddAuditLog(moduleId, actorId, auditComment);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private void AddAuditLog(int moduleId, int actorId, string comment) =>
        dbContext.AuditLogs.Add(new AuditLogEntity
        {
            ActorId = actorId,
            Comment = comment,
            DateCreated = DateTime.UtcNow,
            ModuleId = moduleId,
            Actor = null!
        });
}
