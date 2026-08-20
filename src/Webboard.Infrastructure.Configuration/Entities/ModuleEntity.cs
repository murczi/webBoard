namespace Webboard.Infrastructure.Configuration.Entities;

public class ModuleEntity {
    public int Id { get; set; }

    public int TypeId { get; set; }

    public int? HostId { get; set; }

    public required string FriendlyName { get; set; }

    public string? Description { get; set; }

    public string? HealthCheckUrl { get; set; }

    public string? ManagementUrl { get; set; }

    public bool DeletionFlag { get; set; }

    public bool IsEnabled { get; set; }

    public DateTimeOffset DateCreated { get; set; }

    public DateTimeOffset DateUpdated { get; set; }

    public HostEntity? Host { get; set; }
    public ModuleTypeEntity? Type { get; set; }

    public virtual ICollection<AuditLogEntity> AuditLogs { get; set; }
        = new List<AuditLogEntity>();
}
