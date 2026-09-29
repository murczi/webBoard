namespace Webboard.Domain.Interfaces.Services;
using Model.Modules;
public interface IModuleOperations {
    Task<IReadOnlyList<string>> AllowedServiceOperationsAsync(ModuleModel module, CancellationToken token);
    Task<ModuleOperationResult> ExecuteAsync(int actorId, ModuleOperationRequest request, CancellationToken cancellationToken);
}
public interface IAgentOperationsClient {
    Task<ModuleOperationResult> ExecuteAsync(string baseUrl, Guid requestId, int moduleId, string kind, string target, string operation, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetAllowedOperationsAsync(string baseUrl, string kind, string target, CancellationToken token) => Task.FromResult<IReadOnlyList<string>>([]);
}
