namespace Webboard.Infrastructure.Configuration.Entities;

public class ModuleEntity {
    public int Id { get; set; }

    public int? ManagedHostId { get; set; }

    public int ModuleTypeId { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public required ModuleTypeEntity Type { get; set; }

    public string? TargetIdentifier { get; set; }

    public string? HealthCheckUrl { get; set; }

    public string? ManagementUrl { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool ShowOnOverview { get; set; } = true;

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ManagedHostEntity? ManagedHost { get; set; }

    public virtual ICollection<AuditLogEntity> AuditLogs { get; set; }
        = new List<AuditLogEntity>();
}
