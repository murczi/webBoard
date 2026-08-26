namespace Webboard.Domain.Model.AuditLogs;

public sealed class PagedAuditLogsModel {
    public required IReadOnlyList<AuditLogModel> Items { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalItems { get; init; }
    public int TotalPages => TotalItems == 0 ? 1 : (int)Math.Ceiling((double)TotalItems / PageSize);
}
