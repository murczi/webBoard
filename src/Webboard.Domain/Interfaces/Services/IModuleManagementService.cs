namespace Webboard.Domain.Interfaces.Services;

using Model.Modules;

public interface IModuleManagementService {
    Task<IReadOnlyList<ModuleModel>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ModuleOptionModel>> GetHostsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ModuleOptionModel>> GetTypesAsync(CancellationToken cancellationToken = default);
    Task<ModuleModel> AddAsync(ModuleModel module, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(ModuleModel module, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int moduleId, CancellationToken cancellationToken = default);
}
