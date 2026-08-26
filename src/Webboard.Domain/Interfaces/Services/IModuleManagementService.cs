namespace Webboard.Domain.Interfaces.Services;

using Model.Modules;

public interface IModuleManagementService {
    Task<IReadOnlyList<ModuleModel>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ModuleOptionModel>> GetHostsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ModuleOptionModel>> GetTypesAsync(CancellationToken cancellationToken = default);
    Task<ModuleModel> AddAsync(ModuleModel module, int actorId, string auditComment, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(ModuleModel module, int actorId, string auditComment, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int moduleId, int actorId, string auditComment, CancellationToken cancellationToken = default);
}
