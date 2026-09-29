namespace Webboard.Domain.Model.AuditLogs;

public enum AuditAction { Legacy, Create, Update, Delete, PermissionsChanged }

public sealed record AuditLogQuery {
    public string? Resource { get; init; }
    public AuditAction? Action { get; init; }
    public int? ActorId { get; init; }
    public int? TargetId { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? UntilUtc { get; init; }
    public int Page { get; init; } = 1;
}

public sealed record AuditActorModel(int Id, string Name);
