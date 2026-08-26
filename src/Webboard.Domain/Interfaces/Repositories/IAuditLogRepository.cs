namespace Webboard.Domain.Interfaces.Repositories;

using Model.AuditLogs;

public interface IAuditLogRepository {
    Task<PagedAuditLogsModel> GetModuleLogsAsync(
        int moduleId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PagedAuditLogsModel> GetHostLogsAsync(
        int hostId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PagedAuditLogsModel> GetUserLogsAsync(
        int userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
