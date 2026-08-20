namespace Webboard.Infrastructure.Configuration.Entities;

public class AuditLogEntity {
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
