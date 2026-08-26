namespace Webboard.Domain.Interfaces.Repositories;

using Model.Authentication;

public interface IUserAuthenticationRepository {
    Task<UserLoginModel?> FindByNameAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task<bool> UserNameExistsAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task<bool> CreateAsync(
        string name,
        string passwordHash,
        CancellationToken cancellationToken = default);
}
