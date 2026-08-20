namespace Webboard.Infrastructure.Configuration.Entities;

public class UserEntity {
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string PasswordHash { get; set; }

    public bool DeletionFlag { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime DateUpdated { get; set; }

    public ICollection<AuditLogEntity> AuditLogsAsActor { get; set; }
        = new List<AuditLogEntity>();

    public ICollection<AuditLogEntity> AuditLogsAsTarget { get; set; }
        = new List<AuditLogEntity>();
}
