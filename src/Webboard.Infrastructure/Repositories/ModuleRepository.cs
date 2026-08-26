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
                Name = module.FriendlyName,
                Description = module.Description,
                HealthCheckUrl = module.HealthCheckUrl,
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

    public Task<bool> TypeExistsAsync(int typeId, CancellationToken cancellationToken = default) =>
        dbContext.ModuleTypes.AnyAsync(type => type.Id == typeId, cancellationToken);

    public async Task AddAsync(ModuleModel module, CancellationToken cancellationToken = default) {
        var entity = new ModuleEntity
        {
            TypeId = module.TypeId,
            HostId = module.HostId,
            FriendlyName = module.Name,
            Description = module.Description,
            HealthCheckUrl = module.HealthCheckUrl,
            ManagementUrl = module.ManagementUrl,
            DeletionFlag = false,
            IsEnabled = module.IsEnabled,
            DateCreated = module.DateCreated,
            DateUpdated = module.DateUpdated
        };
        dbContext.Modules.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        module.Id = entity.Id;
    }

    public async Task<bool> UpdateAsync(ModuleModel module, CancellationToken cancellationToken = default) =>
        await dbContext.Modules
            .Where(entity => entity.Id == module.Id && !entity.DeletionFlag)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(entity => entity.TypeId, module.TypeId)
                    .SetProperty(entity => entity.HostId, module.HostId)
                    .SetProperty(entity => entity.FriendlyName, module.Name)
                    .SetProperty(entity => entity.Description, module.Description)
                    .SetProperty(entity => entity.HealthCheckUrl, module.HealthCheckUrl)
                    .SetProperty(entity => entity.ManagementUrl, module.ManagementUrl)
                    .SetProperty(entity => entity.IsEnabled, module.IsEnabled)
                    .SetProperty(entity => entity.DateUpdated, module.DateUpdated),
                cancellationToken) > 0;

    public async Task<bool> DeleteAsync(int moduleId, CancellationToken cancellationToken = default) =>
        await dbContext.Modules
            .Where(module => module.Id == moduleId && !module.DeletionFlag)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(module => module.DeletionFlag, true)
                    .SetProperty(module => module.IsEnabled, false)
                    .SetProperty(module => module.DateUpdated, DateTimeOffset.UtcNow),
                cancellationToken) > 0;
}
