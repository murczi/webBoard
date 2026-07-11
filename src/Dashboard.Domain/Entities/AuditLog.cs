namespace Dashboard.Domain.Entities;

public sealed class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? DashboardModuleId { get; set; }

    public Guid? ActorUserId { get; set; }

    public string? ActorDisplayName { get; set; }

    public required string Action { get; set; }

    public bool Succeeded { get; set; }

    public string? Details { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }
        = DateTimeOffset.UtcNow;

    public DashboardModule? Module { get; set; }
}