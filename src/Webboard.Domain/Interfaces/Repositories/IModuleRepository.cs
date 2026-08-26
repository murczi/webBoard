namespace Webboard.Domain.Interfaces.Repositories;

using Model.Modules;

public interface IModuleRepository {
    Task<IReadOnlyList<ModuleModel>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ModuleOptionModel>> GetHostsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ModuleOptionModel>> GetTypesAsync(CancellationToken cancellationToken = default);
    Task<bool> HostExistsAsync(int hostId, CancellationToken cancellationToken = default);
    Task<bool> TypeExistsAsync(int typeId, CancellationToken cancellationToken = default);
    Task AddAsync(ModuleModel module, int actorId, string auditComment, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(ModuleModel module, int actorId, string auditComment, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int moduleId, int actorId, string auditComment, CancellationToken cancellationToken = default);
}
