namespace Webboard.Infrastructure.Configuration.Entities;

public class HostEntity {
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string AgentBaseUrl { get; set; }

    public bool DeletionFlag { get; set; }

    public bool IsEnabled { get; set; }

    public DateTime DateCreated { get; set; }

    public ICollection<AuditLogEntity> AuditLogs { get; set; }
        = new List<AuditLogEntity>();

    public ICollection<ModuleEntity> Modules { get; set; }
        = new List<ModuleEntity>();
}
