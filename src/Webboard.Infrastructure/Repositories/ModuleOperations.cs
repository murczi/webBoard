namespace Webboard.Infrastructure.Repositories;
using Configuration;
using Configuration.Entities;
using Domain.Interfaces.Services;
using Domain.Model.Modules;
using Domain.Model.AuditLogs;
using Microsoft.EntityFrameworkCore;
using Npgsql;

public sealed class ModuleOperations(WebboardDbContext db, IAgentOperationsClient agent) : IModuleOperations {
    public async Task<IReadOnlyList<string>> AllowedServiceOperationsAsync(ModuleModel module, CancellationToken token) {
        if (!module.IsEnabled || module.HostAgentBaseUrl is null || module.TypeName is not ("Docker" or "Systemd")) return [];
        try { return await agent.GetAllowedOperationsAsync(module.HostAgentBaseUrl, module.TypeName, module.TypeName == "Docker" ? module.ContainerId ?? "" : module.ServiceName ?? "", token); }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException) { return []; }
    }
    public async Task<ModuleOperationResult> ExecuteAsync(int actorId, ModuleOperationRequest request, CancellationToken cancellationToken) {
        if (request.RequestId == Guid.Empty) throw new ArgumentException("A request ID is required.");
        var access = await db.UserCrudAccess.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == actorId && x.Resource == "Modules" && !x.User.DeletionFlag, cancellationToken);
        if (access is null || !access.CanRead || !access.CanOperate) throw new UnauthorizedAccessException();
        if (request.Operation is not ("start" or "stop" or "restart" or "enable" or "disable"))
            throw new ArgumentException("Unsupported operation.");
        var module = await db.Modules.Include(x => x.Type).Include(x => x.Host)
            .SingleOrDefaultAsync(x => x.Id == request.ModuleId && !x.DeletionFlag && x.IsEnabled, cancellationToken)
            ?? throw new ArgumentException("Module is missing or disabled.");
        var kind = module.Type?.Name ?? "";
        var host = module.Host;
        var target = kind == "Docker" ? module.ContainerId : module.ServiceName;
        if (kind is not ("Docker" or "Systemd") || kind == "Docker" && request.Operation is "enable" or "disable")
            throw new ArgumentException("Operation is not available for this module.");
        if (host is null || !host.IsEnabled || host.DeletionFlag || string.IsNullOrWhiteSpace(target)) throw new ArgumentException("Operation host is missing or disabled.");

        var existing = await db.AuditLogs.AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == request.RequestId, cancellationToken);
        if (existing is not null) return Previous(existing, actorId, module.Id, request.Operation);
        var audit = new AuditLogEntity {
            ActorId = actorId, Actor = null!, ModuleId = module.Id, HostId = host.Id,
            DateCreated = DateTime.UtcNow, Comment = "Requested module operation.",
            Action = AuditAction.ServiceControl,
            OperationId = request.RequestId, Operation = request.Operation, Outcome = "Pending",
            TargetSnapshot = $"{module.FriendlyName} / {host.Name} / {target}"[..Math.Min(300, $"{module.FriendlyName} / {host.Name} / {target}".Length)]
        };
        db.AuditLogs.Add(audit);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) {
            db.ChangeTracker.Clear();
            return Previous(await db.AuditLogs.SingleAsync(x => x.OperationId == request.RequestId, cancellationToken), actorId, module.Id, request.Operation);
        }
        ModuleOperationResult result;
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        var locked = false;
        try {
            await connection.OpenAsync(cancellationToken);
            await using var acquire = new NpgsqlCommand("SELECT pg_try_advisory_lock(78211, @host)", connection);
            acquire.Parameters.AddWithValue("host", host.Id);
            locked = (bool)(await acquire.ExecuteScalarAsync(cancellationToken))!;
            result = locked ? await agent.ExecuteAsync(host.AgentBaseUrl, request.RequestId, module.Id, kind, target, request.Operation, cancellationToken)
                : new(false, "Another operation is running on this host.");
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or NpgsqlException) {
            result = new(false, "Operation outcome is unknown. Verify the target before issuing another operation.");
        }
        finally {
            if (locked && connection.State == System.Data.ConnectionState.Open) {
                await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(78211, @host)", connection);
                release.Parameters.AddWithValue("host", host.Id);
                try { await release.ExecuteScalarAsync(CancellationToken.None); }
                catch (NpgsqlException) { /* Closing the physical connection releases the host lock. */ NpgsqlConnection.ClearPool(connection); }
            }
        }
        audit.Outcome = result.Success ? "Succeeded" : result.Message.Contains("unknown", StringComparison.OrdinalIgnoreCase) ? "Unknown" : result.TimedOut ? "TimedOut" : "Failed";
        audit.ExitCode = result.ExitCode;
        audit.Output = result.Output is { Length: > 16384 } ? result.Output[..16384] : result.Output;
        audit.Failure = result.Success ? null : result.Message[..Math.Min(result.Message.Length, 1000)];
        audit.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        return result;
    }
    private static ModuleOperationResult Previous(AuditLogEntity audit, int actorId, int moduleId, string operation) {
        if (audit.ActorId != actorId || audit.ModuleId != moduleId || audit.Operation != operation) throw new ArgumentException("Request ID has already been used.");
        return new(audit.Outcome == "Succeeded", audit.Outcome == "Pending" ? "Operation was already submitted; verify target state before retrying." : audit.Failure ?? "Operation completed.", audit.ExitCode, audit.Output);
    }
}
