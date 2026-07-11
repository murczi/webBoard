using Dashboard.Domain.Modules;

namespace Dashboard.Domain.Entities;

public sealed class DashboardModule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? ManagedHostId { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public ModuleType Type { get; set; }

    public string? TargetIdentifier { get; set; }

    public string? HealthCheckUrl { get; set; }

    public string? ManagementUrl { get; set; }

    public bool IsEnabled { get; set; } = true;

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ManagedHost? ManagedHost { get; set; }

    public ModuleStatus? CurrentStatus { get; set; }

    public ICollection<AuditLog> AuditLogs { get; set; }
        = new List<AuditLog>();
}