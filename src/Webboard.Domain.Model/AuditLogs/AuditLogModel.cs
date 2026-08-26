namespace Webboard.Domain.Model.AuditLogs;

public sealed class AuditLogModel {
    public const int MaxCommentLength = 100;

    public required string ActorName { get; init; }
    public required string Comment { get; init; }
    public DateTime DateCreated { get; init; }
}
