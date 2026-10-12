namespace Webboard.Infrastructure.Repositories;
using Configuration;
using Configuration.Entities;
using Domain.Interfaces.Repositories;
using Domain.Model.Modules;
using Domain.Model.AuditLogs;
using Microsoft.EntityFrameworkCore;
using Npgsql;

public sealed class PostgresModuleOperationStore(WebboardDbContext db) : IModuleOperationStore {
    public async Task<ModuleModel?> FindTargetAsync(int moduleId, CancellationToken token) =>
        await db.Modules.AsNoTracking().Where(x => x.Id == moduleId && !x.DeletionFlag).Select(ModuleProjection.Read).SingleOrDefaultAsync(token);
    public async Task<ReservedModuleOperation> ReserveAsync(int actorId, ModuleOperationRequest request, ModuleModel target, DateTimeOffset at, CancellationToken token) {
        var existing = await db.AuditLogs.AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == request.RequestId, token);
        if (existing is not null) return Previous(existing, actorId, request);
        var snapshot = $"{target.Name} / {target.HostName} / {(target.TypeName == "Docker" ? target.ContainerId : target.ServiceName)}";
        db.AuditLogs.Add(new AuditLogEntity { ActorId = actorId, Actor = null!, ModuleId = target.Id, HostId = target.HostId,
            DateCreated = at, Comment = "Requested module operation.", Action = AuditAction.ServiceControl,
            OperationId = request.RequestId, Operation = request.Operation, Outcome = "Pending", TargetSnapshot = snapshot[..Math.Min(snapshot.Length, 300)] });
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_AuditLogs_OperationId" }) {
            db.ChangeTracker.Clear();
            return Previous(await db.AuditLogs.AsNoTracking().SingleAsync(x => x.OperationId == request.RequestId, token), actorId, request);
        }
        return new(true, new(false, "Operation pending.") { Outcome = OperationOutcome.Unknown });
    }
    private static ReservedModuleOperation Previous(AuditLogEntity audit, int actorId, ModuleOperationRequest request) {
        if (audit.ActorId != actorId || audit.ModuleId != request.ModuleId || audit.Operation != request.Operation) throw new ArgumentException("Request ID has already been used.");
        return new(false, new(audit.Outcome == "Succeeded", audit.Outcome == "Pending" ? "Operation was already submitted; verify target state before retrying." : audit.Failure ?? "Operation completed.", audit.ExitCode, audit.Output, audit.TimedOut) {
            Outcome = Enum.TryParse<OperationOutcome>(audit.Outcome, out var outcome) ? outcome : OperationOutcome.Unknown,
            FailureReason = Enum.TryParse<OperationFailureReason>(audit.FailureReason, out var reason) ? reason : OperationFailureReason.None
        });
    }
    public async Task CompleteAsync(Guid requestId, ModuleOperationResult result, DateTimeOffset at, CancellationToken token) {
        var audit = await db.AuditLogs.SingleAsync(x => x.OperationId == requestId, token);
        audit.Outcome = result.Outcome.ToString(); audit.TimedOut = result.TimedOut; audit.FailureReason = result.FailureReason.ToString();
        audit.ExitCode = result.ExitCode; audit.Output = result.Output is { Length: > 16384 } ? result.Output[..16384] : result.Output;
        audit.Failure = result.Success ? null : result.Message[..Math.Min(result.Message.Length, 1000)]; audit.CompletedAt = at;
        await db.SaveChangesAsync(token);
    }
    public async Task<IAsyncDisposable?> TryLockHostAsync(int hostId, CancellationToken token) {
        var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        try {
            await connection.OpenAsync(token);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(78211, @host)", connection);
            command.Parameters.AddWithValue("host", hostId);
            if ((bool)(await command.ExecuteScalarAsync(token))!) return new HostLease(connection, hostId);
            await connection.DisposeAsync(); return null;
        }
        catch (NpgsqlException exception) { await connection.DisposeAsync(); throw new OperationStoreException(exception); }
        catch { await connection.DisposeAsync(); throw; }
    }
    private sealed class HostLease(NpgsqlConnection connection, int hostId) : IAsyncDisposable {
        public async ValueTask DisposeAsync() {
            try {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(78211, @host)", connection);
                command.Parameters.AddWithValue("host", hostId);
                await command.ExecuteScalarAsync(CancellationToken.None);
            }
            catch (NpgsqlException) { NpgsqlConnection.ClearPool(connection); }
            finally { await connection.DisposeAsync(); }
        }
    }
}
