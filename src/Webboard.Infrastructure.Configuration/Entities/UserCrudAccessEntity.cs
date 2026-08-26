namespace Webboard.Infrastructure.Configuration.Entities;

public class UserCrudAccessEntity {
    public int Id { get; set; }

    public int UserId { get; set; }

    public required string Resource { get; set; }

    public bool CanCreate { get; set; }

    public bool CanRead { get; set; }

    public bool CanUpdate { get; set; }

    public bool CanDelete { get; set; }

    public required UserEntity User { get; set; }
}
