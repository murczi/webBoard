namespace Webboard.Infrastructure.Configuration.Entities;

public class AuditLogEntity {
    public int Id { get; set; }

    public int? ModuleId { get; set; }

    public int? ActorUserId { get; set; }

    public required string AuditComment { get; set; }

    public DateTime DateCreated { get; set; }

    public ModuleEntity? Module { get; set; }
}
