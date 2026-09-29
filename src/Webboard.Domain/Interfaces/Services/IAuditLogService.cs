namespace Webboard.Domain.Interfaces.Services;

using Model.AuditLogs;

public interface IAuditLogService {
    Task<PagedAuditLogsModel> SearchAsync(AuditLogQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditActorModel>> GetActorsAsync(CancellationToken cancellationToken = default);

    Task<PagedAuditLogsModel> GetModuleLogsAsync(
        int moduleId,
        int page,
        CancellationToken cancellationToken = default);

    Task<PagedAuditLogsModel> GetHostLogsAsync(
        int hostId,
        int page,
        CancellationToken cancellationToken = default);

    Task<PagedAuditLogsModel> GetUserLogsAsync(
        int userId,
        int page,
        CancellationToken cancellationToken = default);
}
