namespace Webboard.Domain.Model.AuditLogs;

public sealed class AuditLogModel {
    public const int MaxCommentLength = 100;

    public int Id { get; init; }
    public int ActorId { get; init; }
    public string Resource { get; init; } = "Unknown";
    public int? TargetId { get; init; }
    public string? TargetName { get; init; }
    public AuditAction Action { get; init; }
    public required string ActorName { get; init; }
    public required string Comment { get; init; }
    public DateTime DateCreated { get; init; }
}
