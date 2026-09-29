namespace Webboard.Infrastructure.Repositories;

using Configuration;
using Domain.Interfaces.Repositories;
using Domain.Model.AuditLogs;
using Microsoft.EntityFrameworkCore;

public sealed class AuditLogRepository(WebboardDbContext dbContext) : IAuditLogRepository {
    public async Task<IReadOnlyList<AuditActorModel>> GetActorsAsync(CancellationToken cancellationToken = default) =>
        await dbContext.AuditLogs.AsNoTracking()
            .Select(log => new { log.ActorId, log.Actor.Name }).Distinct()
            .OrderBy(actor => actor.Name)
            .Select(actor => new AuditActorModel(actor.ActorId, actor.Name))
            .ToListAsync(cancellationToken);

    public Task<PagedAuditLogsModel> SearchAsync(AuditLogQuery filter, int pageSize, CancellationToken cancellationToken = default) {
        var query = dbContext.AuditLogs.AsNoTracking();
        query = filter.Resource switch {
            "Modules" => query.Where(log => log.ModuleId != null),
            "Hosts" => query.Where(log => log.HostId != null),
            "Users" => query.Where(log => log.UserId != null),
            _ => query
        };
        if (filter.TargetId.HasValue)
            query = filter.Resource switch {
                "Modules" => query.Where(log => log.ModuleId == filter.TargetId),
                "Hosts" => query.Where(log => log.HostId == filter.TargetId),
                "Users" => query.Where(log => log.UserId == filter.TargetId),
                _ => query
            };
        if (filter.Action.HasValue) query = query.Where(log => log.Action == filter.Action);
        if (filter.ActorId.HasValue) query = query.Where(log => log.ActorId == filter.ActorId);
        if (filter.FromUtc.HasValue) query = query.Where(log => log.DateCreated >= filter.FromUtc);
        if (filter.UntilUtc.HasValue) query = query.Where(log => log.DateCreated < filter.UntilUtc);
        return ReadPageAsync(query, filter.Page, pageSize, cancellationToken);
    }

    public Task<PagedAuditLogsModel> GetModuleLogsAsync(
        int moduleId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        GetAsync(log => log.ModuleId == moduleId, page, pageSize, cancellationToken);

    public Task<PagedAuditLogsModel> GetHostLogsAsync(
        int hostId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        GetAsync(log => log.HostId == hostId, page, pageSize, cancellationToken);

    public Task<PagedAuditLogsModel> GetUserLogsAsync(
        int userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        GetAsync(log => log.UserId == userId, page, pageSize, cancellationToken);

    private Task<PagedAuditLogsModel> GetAsync(
        System.Linq.Expressions.Expression<Func<Configuration.Entities.AuditLogEntity, bool>> predicate,
        int page,
        int pageSize,
        CancellationToken cancellationToken) {
        return ReadPageAsync(dbContext.AuditLogs.AsNoTracking().Where(predicate), page, pageSize, cancellationToken);
    }

    private static async Task<PagedAuditLogsModel> ReadPageAsync(
        IQueryable<Configuration.Entities.AuditLogEntity> query, int page, int pageSize,
        CancellationToken cancellationToken) {
        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 1 : (int)Math.Ceiling((double)totalItems / pageSize);
        page = Math.Clamp(page, 1, totalPages);

        var items = await query
            .OrderByDescending(log => log.DateCreated)
            .ThenByDescending(log => log.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(log => new AuditLogModel
            {
                Id = log.Id,
                ActorId = log.ActorId,
                Action = log.Action,
                OperationId = log.OperationId,
                Operation = log.Operation,
                Outcome = log.Outcome,
                ExitCode = log.ExitCode,
                Output = log.Output,
                Failure = log.Failure,
                TargetSnapshot = log.TargetSnapshot,
                CompletedAt = log.CompletedAt,

                Resource = log.ModuleId != null ? "Modules" : log.HostId != null ? "Hosts" : log.UserId != null ? "Users" : "Unknown",
                TargetId = log.ModuleId ?? log.HostId ?? log.UserId,
                TargetName = log.Module != null ? log.Module.FriendlyName : log.Host != null ? log.Host.Name : log.User != null ? log.User.Name : null,
                ActorName = log.Actor.Name,
                Comment = log.Comment,
                DateCreated = log.DateCreated
            })
            .ToListAsync(cancellationToken);

        return new PagedAuditLogsModel
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems
        };
    }
}
