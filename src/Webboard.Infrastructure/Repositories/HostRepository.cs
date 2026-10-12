namespace Webboard.Infrastructure.Repositories;

using Configuration;
using Configuration.Entities;
using Domain.Interfaces.Repositories;
using Domain.Model.Hosts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Domain.Services;

public sealed class HostRepository(WebboardDbContext dbContext) : IHostRepository {
    private readonly TimeProvider timeProvider = TimeProvider.System;
    public HostRepository(WebboardDbContext dbContext, TimeProvider timeProvider) : this(dbContext) => this.timeProvider = timeProvider;

    public async Task<IReadOnlyList<HostModel>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Hosts.AsNoTracking()
            .Where(host => !host.DeletionFlag)
            .OrderBy(host => host.Name)
            .Select(host => new HostModel {
                Id = host.Id,
                Version = host.Version,
                DateUpdated = host.DateUpdated,
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
        var entity = new HostEntity {
            Name = host.Name,
            AgentBaseUrl = host.AgentBaseUrl,
            IsEnabled = host.IsEnabled,
            DeletionFlag = false,
            DateCreated = host.DateCreated,
            DateUpdated = host.DateUpdated == default ? host.DateCreated : host.DateUpdated
        };
        dbContext.Hosts.Add(entity);
        dbContext.AuditLogs.Add(new AuditLogEntity {
            ActorId = actorId,
            Action = Webboard.Domain.Model.AuditLogs.AuditAction.Create,
            Comment = auditComment,
            DateCreated = timeProvider.GetUtcNow(),
            Host = entity,
            Actor = null!
        });
        await TranslateNameConflictAsync(async () => await dbContext.SaveChangesAsync(cancellationToken));
        host.Id = entity.Id; host.Version = entity.Version; host.DateUpdated = entity.DateUpdated;
    }

    public async Task<bool> UpdateAsync(
        HostModel host,
        int actorId,
        string auditComment,
        CancellationToken cancellationToken = default) {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var current = await dbContext.Hosts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == host.Id && !x.DeletionFlag, cancellationToken);
        if (current is null) return false;
        var lifecycleChanged = current.IsEnabled != host.IsEnabled;
        var endpointChanged = current.AgentBaseUrl != host.AgentBaseUrl;
        var dependentModules = await dbContext.Modules.Where(x => x.HostId == host.Id && !x.DeletionFlag &&
            (lifecycleChanged || (endpointChanged && x.Type != null && (x.Type.Name.ToLower() == "docker" || x.Type.Name.ToLower() == "systemd"))))
            .Select(x => x.Id).ToListAsync(cancellationToken);
        await MonitoringInvalidation.LockAsync(dbContext, dependentModules, cancellationToken);
        var updated = await TranslateNameConflictAsync(async () => await dbContext.Hosts
            .Where(entity => entity.Id == host.Id && entity.Version == host.Version && !entity.DeletionFlag)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(entity => entity.Version, entity => entity.Version + 1)
                    .SetProperty(entity => entity.DateUpdated, host.DateUpdated)
                    .SetProperty(entity => entity.Name, host.Name)
                    .SetProperty(entity => entity.AgentBaseUrl, host.AgentBaseUrl)
                    .SetProperty(entity => entity.IsEnabled, host.IsEnabled),
                cancellationToken)) > 0;
        if (!updated) {
            if (await dbContext.Hosts.AnyAsync(x => x.Id == host.Id && !x.DeletionFlag, cancellationToken)) throw new ResourceConflictException();
            return false;
        }

        await dbContext.Modules.Where(x => dependentModules.Contains(x.Id)).ExecuteUpdateAsync(
            setters => setters.SetProperty(x => x.MonitoringVersion, x => x.MonitoringVersion + 1), cancellationToken);
        await MonitoringInvalidation.InvalidateAsync(dbContext, dependentModules, timeProvider, cancellationToken);
        AddAuditLog(host.Id, actorId, auditComment, Webboard.Domain.Model.AuditLogs.AuditAction.Update);
        await TranslateNameConflictAsync(async () => await dbContext.SaveChangesAsync(cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        host.Version++;
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
                    .SetProperty(host => host.Version, host => host.Version + 1)
                    .SetProperty(host => host.DateUpdated, timeProvider.GetUtcNow())
                    .SetProperty(host => host.DeletionFlag, true)
                    .SetProperty(host => host.IsEnabled, false),
                cancellationToken) > 0;
        if (!deleted)
            return false;

        await dbContext.Modules.Where(x => dependentModules.Contains(x.Id)).ExecuteUpdateAsync(
            setters => setters.SetProperty(x => x.MonitoringVersion, x => x.MonitoringVersion + 1), cancellationToken);
        await MonitoringInvalidation.InvalidateAsync(dbContext, dependentModules, timeProvider, cancellationToken);
        AddAuditLog(hostId, actorId, auditComment, Webboard.Domain.Model.AuditLogs.AuditAction.Delete);
        await TranslateNameConflictAsync(async () => await dbContext.SaveChangesAsync(cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task<T> TranslateNameConflictAsync<T>(Func<Task<T>> action) {
        try { return await action(); } catch (DbUpdateException exception) when (exception.InnerException is PostgresException {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Hosts_NormalizedName"
        }) {
            throw new ArgumentException("A host with this name already exists.", exception);
        } catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation && exception.ConstraintName == "IX_Hosts_NormalizedName") {
            throw new ArgumentException("A host with this name already exists.", exception);
        }
    }

    private void AddAuditLog(int hostId, int actorId, string comment, Webboard.Domain.Model.AuditLogs.AuditAction action) =>
        dbContext.AuditLogs.Add(new AuditLogEntity {
            ActorId = actorId,
            Action = action,
            Comment = comment,
            DateCreated = timeProvider.GetUtcNow(),
            HostId = hostId,
            Actor = null!
        });
}
