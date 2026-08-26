namespace Webboard.Domain.Model.UserCrudAccess;

public class UserCrudAccessModel {
    public int Id { get; set; }

    public int UserId { get; set; }

    public required string Resource { get; set; }

    public bool CanCreate { get; set; }

    public bool CanRead { get; set; }

    public bool CanUpdate { get; set; }

    public bool CanDelete { get; set; }

    public bool IsReadOnlyResource { get; set; }
}
