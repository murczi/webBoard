namespace Webboard.Domain.Model.Authentication;

using UserCrudAccess;

public class UserLoginModel {
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string PasswordHash { get; set; }

    public IReadOnlyList<UserCrudAccessModel> Access { get; set; }
        = Array.Empty<UserCrudAccessModel>();
}
