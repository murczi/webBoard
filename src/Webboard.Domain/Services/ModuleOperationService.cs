namespace Webboard.Domain.Services;

using Contracts;
using Interfaces.Repositories;
using Interfaces.Services;
using Model.Modules;

public sealed class ModuleOperationService(IModuleOperationStore store, IAgentOperationsClient agent, IResourceAuthorization access) : IModuleOperations {
    private readonly TimeProvider timeProvider = TimeProvider.System;
    public ModuleOperationService(IModuleOperationStore store, IAgentOperationsClient agent, IResourceAuthorization access, TimeProvider timeProvider)
        : this(store, agent, access) => this.timeProvider = timeProvider;

    public async Task<IReadOnlyList<string>> AllowedServiceOperationsAsync(ModuleModel module, CancellationToken token) {
        if (!ModuleEligibility.CanOperate(module) || module.HostAgentBaseUrl is not { } baseUrl) return [];
        try { return await agent.GetAllowedOperationsAsync(baseUrl, ModuleTypes.CanonicalName(module.TypeName), ModuleTypes.Is(module.TypeName, ModuleTypes.Docker) ? module.ContainerId! : module.ServiceName!, token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return []; }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException) { return []; }
    }
    public async Task<ModuleOperationResult> ExecuteAsync(int actorId, ModuleOperationRequest request, CancellationToken cancellationToken) {
        if (request.RequestId == Guid.Empty) throw new ArgumentException("A request ID is required.");
        await access.RequireAsync(actorId, "Modules", "read", cancellationToken);
        await access.RequireAsync(actorId, "Modules", "operations", cancellationToken);
        if (request.Operation is not ("start" or "stop" or "restart" or "enable" or "disable")) throw new ArgumentException("Unsupported operation.");
        var module = await store.FindTargetAsync(request.ModuleId, cancellationToken);
        if (module is null || !ModuleEligibility.CanOperate(module)) throw new ArgumentException("Operation module or host is missing or disabled.");
        if (ModuleTypes.Is(module.TypeName, ModuleTypes.Docker) && request.Operation is "enable" or "disable") throw new ArgumentException("Operation is not available for this module.");
        var reserved = await store.ReserveAsync(actorId, request, module, timeProvider.GetUtcNow(), cancellationToken);
        if (!reserved.Created) return reserved.Result;
        ModuleOperationResult result;
        var dispatched = false;
        try {
            await using var lease = await store.TryLockHostAsync(module.HostId!.Value, cancellationToken);
            if (lease is null) result = new(false, "Another operation is running on this host.") { Outcome = OperationOutcome.Rejected, FailureReason = OperationFailureReason.Busy };
            else {
                dispatched = true;
                result = await agent.ExecuteAsync(module.HostAgentBaseUrl!, request.RequestId, module.Id, ModuleTypes.CanonicalName(module.TypeName),
                    ModuleTypes.Is(module.TypeName, ModuleTypes.Docker) ? module.ContainerId! : module.ServiceName!, request.Operation, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or OperationStoreException) {
            var timeout = exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;
            result = new(false, dispatched ? "Operation outcome is unknown. Verify target state before retrying." : "Operation was not dispatched.", TimedOut: timeout) {
                Outcome = dispatched ? OperationOutcome.Unknown : OperationOutcome.Failed,
                FailureReason = exception is OperationCanceledException ? timeout ? OperationFailureReason.Timeout : OperationFailureReason.Interrupted : OperationFailureReason.Transport
            };
        }
        await store.CompleteAsync(request.RequestId, result, timeProvider.GetUtcNow(), CancellationToken.None);
        return result;
    }
}
