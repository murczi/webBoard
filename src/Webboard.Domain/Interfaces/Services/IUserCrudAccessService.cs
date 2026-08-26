namespace Webboard.Domain.Interfaces.Services;

using Model.UserCrudAccess;

public interface IUserCrudAccessService {
    Task<IReadOnlyList<UserSummaryModel>> GetUsersAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserCrudAccessModel>> GetAccessAsync(
        int userId,
        CancellationToken cancellationToken = default);

    Task SaveAccessAsync(
        int userId,
        IReadOnlyCollection<UserCrudAccessModel> access,
        CancellationToken cancellationToken = default);
}
