namespace Webboard.Domain.Interfaces.Repositories;
using Model.Modules;

public sealed record ReservedModuleOperation(bool Created, ModuleOperationResult Result);
public sealed class OperationStoreException(Exception inner) : Exception("Operation persistence is unavailable.", inner);
public interface IModuleOperationStore {
    Task<ModuleModel?> FindTargetAsync(int moduleId, CancellationToken token);
    Task<ReservedModuleOperation> ReserveAsync(int actorId, ModuleOperationRequest request, ModuleModel target, DateTimeOffset at, CancellationToken token);
    Task<IAsyncDisposable?> TryLockHostAsync(int hostId, CancellationToken token);
    Task CompleteAsync(Guid requestId, ModuleOperationResult result, DateTimeOffset at, CancellationToken token);
}
