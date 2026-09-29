namespace Webboard.Infrastructure.Configuration.Entities;

public class AuditLogEntity {
    public Webboard.Domain.Model.AuditLogs.AuditAction Action { get; set; }

    public Guid? OperationId { get; set; }
    public string? Operation { get; set; }
    public string? Outcome { get; set; }
    public int? ExitCode { get; set; }
    public string? Output { get; set; }
    public string? Failure { get; set; }
    public string? TargetSnapshot { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public int Id { get; set; }

    public int ActorId { get; set; }

    public required string Comment { get; set; }

    public DateTime DateCreated { get; set; }

    public int? ModuleId { get; set; }

    public int? HostId { get; set; }

    public int? UserId { get; set; }

    public required UserEntity Actor { get; set; }

    public ModuleEntity? Module { get; set; }

    public HostEntity? Host { get; set; }

    public UserEntity? User { get; set; }
}
