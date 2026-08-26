namespace Webboard.Domain.Interfaces.Repositories;

using Model.UserCrudAccess;

public interface IUserCrudAccessRepository {
    Task<IReadOnlyList<UserSummaryModel>> GetUsersAsync(
        CancellationToken cancellationToken = default);

    Task<bool> UserExistsAsync(
        int userId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteUserAsync(
        int userId,
        int actorId,
        string auditComment,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserCrudAccessModel>> GetByUserIdAsync(
        int userId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        UserCrudAccessModel access,
        CancellationToken cancellationToken = default);

    void Update(UserCrudAccessModel access);

    void Delete(UserCrudAccessModel access);

    void AddAuditLog(int actorId, int userId, string auditComment);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
