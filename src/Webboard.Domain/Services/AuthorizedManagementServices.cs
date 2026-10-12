namespace Webboard.Domain.Services;

using Interfaces.Services;
using Model.Hosts;
using Model.Modules;

public sealed class AuthorizedHostManagementService(HostManagementService inner, IResourceAuthorization access) : IHostManagementService {
    public Task<IReadOnlyList<HostModel>> GetAllAsync(CancellationToken cancellationToken = default) => inner.GetAllAsync(cancellationToken);
    public Task<AgentHealthResult> TestAgentAsync(string url, CancellationToken cancellationToken = default) => inner.TestAgentAsync(url, cancellationToken);
    public async Task<HostModel> AddAsync(HostModel host, int actorId, string comment, CancellationToken cancellationToken = default) {
        await access.RequireAsync(actorId, "Hosts", "create", cancellationToken);
        return await inner.AddAsync(host, actorId, comment, cancellationToken);
    }
    public async Task<bool> UpdateAsync(HostModel host, int actorId, string comment, CancellationToken cancellationToken = default) {
        await access.RequireAsync(actorId, "Hosts", "update", cancellationToken);
        return await inner.UpdateAsync(host, actorId, comment, cancellationToken);
    }
    public async Task<bool> DeleteAsync(int hostId, int actorId, string comment, CancellationToken cancellationToken = default) {
        await access.RequireAsync(actorId, "Hosts", "delete", cancellationToken);
        return await inner.DeleteAsync(hostId, actorId, comment, cancellationToken);
    }
}

public sealed class AuthorizedModuleManagementService(ModuleManagementService inner, IResourceAuthorization access) : IModuleManagementService {
    public Task<IReadOnlyList<ModuleModel>> GetAllAsync(CancellationToken cancellationToken = default) => inner.GetAllAsync(cancellationToken);
    public Task<IReadOnlyList<ModuleOptionModel>> GetHostsAsync(CancellationToken cancellationToken = default) => inner.GetHostsAsync(cancellationToken);
    public Task<IReadOnlyList<ModuleOptionModel>> GetTypesAsync(CancellationToken cancellationToken = default) => inner.GetTypesAsync(cancellationToken);
    public async Task<ModuleModel> AddAsync(ModuleModel module, int actorId, string comment, CancellationToken cancellationToken = default) {
        await access.RequireAsync(actorId, "Modules", "create", cancellationToken);
        return await inner.AddAsync(module, actorId, comment, cancellationToken);
    }
    public async Task<bool> UpdateAsync(ModuleModel module, int actorId, string comment, CancellationToken cancellationToken = default) {
        await access.RequireAsync(actorId, "Modules", "update", cancellationToken);
        return await inner.UpdateAsync(module, actorId, comment, cancellationToken);
    }
    public async Task<bool> DeleteAsync(int moduleId, int actorId, string comment, CancellationToken cancellationToken = default) {
        await access.RequireAsync(actorId, "Modules", "delete", cancellationToken);
        return await inner.DeleteAsync(moduleId, actorId, comment, cancellationToken);
    }
}
