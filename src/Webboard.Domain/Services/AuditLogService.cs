namespace Webboard.Domain.Services;

using Interfaces.Repositories;
using Interfaces.Services;
using Model.AuditLogs;

public sealed class AuditLogService(IAuditLogRepository repository) : IAuditLogService {
    private const int PageSize = 10;

    public Task<PagedAuditLogsModel> SearchAsync(AuditLogQuery query, CancellationToken cancellationToken = default) {
        if (query.Resource is not null && query.Resource is not ("Modules" or "Hosts" or "Users"))
            throw new ArgumentException("Choose a valid resource type.");
        if (query.TargetId.HasValue && (query.TargetId <= 0 || query.Resource is null))
            throw new ArgumentException("A target requires a resource type and a positive ID.");
        if (query.ActorId <= 0 || (query.Action.HasValue && !Enum.IsDefined(query.Action.Value)))
            throw new ArgumentException("Choose a valid actor and action.");
        if (query.FromUtc.HasValue && query.UntilUtc.HasValue && query.FromUtc >= query.UntilUtc)
            throw new ArgumentException("The end date must be on or after the start date.");
        return repository.SearchAsync(query with { Page = NormalizePage(query.Page) }, 25, cancellationToken);
    }

    public Task<IReadOnlyList<AuditActorModel>> GetActorsAsync(CancellationToken cancellationToken = default) =>
        repository.GetActorsAsync(cancellationToken);

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
