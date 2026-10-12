namespace Webboard.Domain.Model.AuditLogs;

public enum AuditAction { Legacy = 0, Create = 1, Update = 2, Delete = 3, PermissionsChanged = 4, ServiceControl = 5, CommandExecution = 6 }

public sealed record AuditLogQuery {
    public string? Resource { get; init; }
    public AuditAction? Action { get; init; }
    public int? ActorId { get; init; }
    public int? TargetId { get; init; }
    public DateTimeOffset? FromUtc { get; init; }
    public DateTimeOffset? UntilUtc { get; init; }
    public int Page { get; init; } = 1;
}

public sealed record AuditActorModel(int Id, string Name);
