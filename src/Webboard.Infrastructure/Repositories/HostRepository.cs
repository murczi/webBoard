namespace Webboard.Infrastructure.Repositories;

using Configuration;
using Configuration.Entities;
using Domain.Interfaces.Repositories;
using Domain.Model.Hosts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

public class HostRepository(WebboardDbContext dbContext) : IHostRepository {
    private readonly TimeProvider timeProvider = TimeProvider.System;
    public HostRepository(WebboardDbContext dbContext, TimeProvider timeProvider) : this(dbContext) => this.timeProvider = timeProvider;

    public async Task<IReadOnlyList<HostModel>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Hosts.AsNoTracking()
            .Where(host => !host.DeletionFlag)
            .OrderBy(host => host.Name)
            .Select(host => new HostModel
            {
                Id = host.Id,
                Name = host.Name,
                AgentBaseUrl = host.AgentBaseUrl,
                IsEnabled = host.IsEnabled,
                DateCreated = host.DateCreated
            })
            .ToListAsync(cancellationToken);

    public async Task<bool> NameExistsAsync(string name, int? excludingHostId = null, CancellationToken cancellationToken = default) {
        var excluded = excludingHostId ?? 0;
        return await dbContext.Database.SqlQuery<int>($"""
            SELECT COUNT(*)::integer AS "Value" FROM "Hosts"
            WHERE "NormalizedName" = lower({name}) AND "Id" <> {excluded}
            """).SingleAsync(cancellationToken) > 0;
    }

    public async Task AddAsync(
        HostModel host,
        int actorId,
        string auditComment,
        CancellationToken cancellationToken = default) {
        var entity = new HostEntity
        {
            Name = host.Name,
            AgentBaseUrl = host.AgentBaseUrl,
            IsEnabled = host.IsEnabled,
            DeletionFlag = false,
            DateCreated = host.DateCreated
        };
        dbContext.Hosts.Add(entity);
        dbContext.AuditLogs.Add(new AuditLogEntity
        {
            ActorId = actorId,
            Action = Webboard.Domain.Model.AuditLogs.AuditAction.Create,
            Comment = auditComment,
            DateCreated = timeProvider.GetUtcNow(),
            Host = entity,
            Actor = null!
        });
        await TranslateNameConflictAsync(async () => await dbContext.SaveChangesAsync(cancellationToken));
        host.Id = entity.Id;
    }

    public async Task<bool> UpdateAsync(
        HostModel host,
        int actorId,
        string auditComment,
        CancellationToken cancellationToken = default) {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var dependentModules = await dbContext.Modules.Where(x => x.HostId == host.Id && !x.DeletionFlag).Select(x => x.Id).ToListAsync(cancellationToken);
        await MonitoringInvalidation.LockAsync(dbContext, dependentModules, cancellationToken);
        var updated = await TranslateNameConflictAsync(async () => await dbContext.Hosts
            .Where(entity => entity.Id == host.Id && !entity.DeletionFlag)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(entity => entity.Name, host.Name)
                    .SetProperty(entity => entity.AgentBaseUrl, host.AgentBaseUrl)
                    .SetProperty(entity => entity.IsEnabled, host.IsEnabled),
                cancellationToken)) > 0;
        if (!updated)
            return false;

        await MonitoringInvalidation.InvalidateAsync(dbContext, dependentModules, timeProvider, cancellationToken);
        AddAuditLog(host.Id, actorId, auditComment, Webboard.Domain.Model.AuditLogs.AuditAction.Update);
        await TranslateNameConflictAsync(async () => await dbContext.SaveChangesAsync(cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(
        int hostId,
        int actorId,
        string auditComment,
        CancellationToken cancellationToken = default) {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var dependentModules = await dbContext.Modules.Where(x => x.HostId == hostId && !x.DeletionFlag).Select(x => x.Id).ToListAsync(cancellationToken);
        await MonitoringInvalidation.LockAsync(dbContext, dependentModules, cancellationToken);
        var deleted = await dbContext.Hosts
            .Where(host => host.Id == hostId && !host.DeletionFlag)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(host => host.DeletionFlag, true)
                    .SetProperty(host => host.IsEnabled, false),
                cancellationToken) > 0;
        if (!deleted)
            return false;

        await MonitoringInvalidation.InvalidateAsync(dbContext, dependentModules, timeProvider, cancellationToken);
        AddAuditLog(hostId, actorId, auditComment, Webboard.Domain.Model.AuditLogs.AuditAction.Delete);
        await TranslateNameConflictAsync(async () => await dbContext.SaveChangesAsync(cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task<T> TranslateNameConflictAsync<T>(Func<Task<T>> action) {
        try { return await action(); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Hosts_NormalizedName" }) {
            throw new ArgumentException("A host with this name already exists.", exception);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation && exception.ConstraintName == "IX_Hosts_NormalizedName") {
            throw new ArgumentException("A host with this name already exists.", exception);
        }
    }

    private void AddAuditLog(int hostId, int actorId, string comment, Webboard.Domain.Model.AuditLogs.AuditAction action) =>
        dbContext.AuditLogs.Add(new AuditLogEntity
        {
            ActorId = actorId,
            Action = action,
            Comment = comment,
            DateCreated = timeProvider.GetUtcNow(),
            HostId = hostId,
            Actor = null!
        });
}
