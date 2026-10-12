namespace Webboard.Infrastructure.Repositories;
using Configuration;
using Domain.Interfaces.Services;
using Domain.Model.Modules;
using Domain.Services;

// Compatibility facade for direct callers; application DI uses the domain coordinator.
public sealed class PostgresModuleOperations(WebboardDbContext db, IAgentOperationsClient agent) : IModuleOperations {
    private readonly ModuleOperationService inner = new(new PostgresModuleOperationStore(db), agent, new ResourceAuthorization(new UserCrudAccessRepository(db)));
    public Task<IReadOnlyList<string>> AllowedServiceOperationsAsync(ModuleModel module, CancellationToken token) => inner.AllowedServiceOperationsAsync(module, token);
    public Task<ModuleOperationResult> ExecuteAsync(int actorId, ModuleOperationRequest request, CancellationToken cancellationToken) => inner.ExecuteAsync(actorId, request, cancellationToken);
}
