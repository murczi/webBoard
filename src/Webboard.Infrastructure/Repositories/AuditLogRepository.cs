namespace Webboard.Infrastructure.Repositories;

using Configuration;
using Domain.Interfaces.Repositories;
using Domain.Model.AuditLogs;
using Microsoft.EntityFrameworkCore;

public sealed class AuditLogRepository(WebboardDbContext dbContext) : IAuditLogRepository {
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

    private async Task<PagedAuditLogsModel> GetAsync(
        System.Linq.Expressions.Expression<Func<Configuration.Entities.AuditLogEntity, bool>> predicate,
        int page,
        int pageSize,
        CancellationToken cancellationToken) {
        var query = dbContext.AuditLogs.AsNoTracking().Where(predicate);
        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 1 : (int)Math.Ceiling((double)totalItems / pageSize);
        page = Math.Min(page, totalPages);

        var items = await query
            .OrderByDescending(log => log.DateCreated)
            .ThenByDescending(log => log.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(log => new AuditLogModel
            {
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
