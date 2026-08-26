namespace Webboard.Domain.Services;

using Interfaces.Repositories;
using Interfaces.Services;
using Model.AuditLogs;

public sealed class AuditLogService(IAuditLogRepository repository) : IAuditLogService {
    private const int PageSize = 10;

    public Task<PagedAuditLogsModel> GetModuleLogsAsync(
        int moduleId,
        int page,
        CancellationToken cancellationToken = default) =>
        repository.GetModuleLogsAsync(moduleId, NormalizePage(page), PageSize, cancellationToken);

    public Task<PagedAuditLogsModel> GetHostLogsAsync(
        int hostId,
        int page,
        CancellationToken cancellationToken = default) =>
        repository.GetHostLogsAsync(hostId, NormalizePage(page), PageSize, cancellationToken);

    public Task<PagedAuditLogsModel> GetUserLogsAsync(
        int userId,
        int page,
        CancellationToken cancellationToken = default) =>
        repository.GetUserLogsAsync(userId, NormalizePage(page), PageSize, cancellationToken);

    private static int NormalizePage(int page) => Math.Max(1, page);
}
