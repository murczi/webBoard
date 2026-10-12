namespace Webboard.Domain.Services;

using Interfaces.Repositories;
using Interfaces.Services;
using Model.UserCrudAccess;

public class UserCrudAccessService(IUserCrudAccessRepository repository)
    : IUserCrudAccessService {
    private static readonly IReadOnlyDictionary<string, bool> Resources = PermissionResources.All
        .ToDictionary(x => x.Key, x => x.IsReadOnly, StringComparer.Ordinal);

    public Task<IReadOnlyList<UserSummaryModel>> GetUsersAsync(
        CancellationToken cancellationToken = default) =>
        repository.GetUsersAsync(cancellationToken);

    public async Task<bool> DeleteUserAsync(
        int userId,
        int actorId,
        string auditComment,
        CancellationToken cancellationToken = default) {
        await RequireUserPermissionAsync(actorId, delete: true, cancellationToken);
        if (userId == actorId)
            throw new UnauthorizedAccessException("You cannot delete your own account.");
        return await repository.DeleteUserAsync(
            userId,
            actorId,
            AuditComment.Normalize(auditComment),
            cancellationToken);
    }

    public async Task<IReadOnlyList<UserCrudAccessModel>> GetAccessAsync(
        int userId,
        CancellationToken cancellationToken = default) {
        if (!await repository.UserExistsAsync(userId, cancellationToken))
            throw new KeyNotFoundException($"User {userId} was not found.");

        var persisted = (await repository.GetByUserIdAsync(userId, cancellationToken))
            .ToDictionary(access => access.Resource, StringComparer.Ordinal);

        return Resources.Select(resource => {
            if (persisted.TryGetValue(resource.Key, out var access)) {
                access.IsReadOnlyResource = resource.Value;
                return access;
            }

            return new UserCrudAccessModel
            {
                UserId = userId,
                Resource = resource.Key,
                IsReadOnlyResource = resource.Value
            };
        }).ToList();
    }

    public async Task SaveAccessAsync(
        int userId,
        IReadOnlyCollection<UserCrudAccessModel> access,
        int actorId,
        string auditComment,
        CancellationToken cancellationToken = default) {
        await RequireUserPermissionAsync(actorId, delete: false, cancellationToken);
        auditComment = AuditComment.Normalize(auditComment);
        if (!await repository.UserExistsAsync(userId, cancellationToken))
            throw new KeyNotFoundException($"User {userId} was not found.");

        var submitted = access.ToDictionary(item => item.Resource, StringComparer.Ordinal);
        if (submitted.Keys.Any(resource => !Resources.ContainsKey(resource)))
            throw new ArgumentException("An unknown CRUD resource was submitted.", nameof(access));

        var normalized = new List<UserCrudAccessModel>();
        foreach (var resource in Resources) {
            if (!submitted.TryGetValue(resource.Key, out var item)) continue;
            item.UserId = userId; PermissionResources.Normalize(item); normalized.Add(item);
        }
        await repository.ReplaceAccessAsync(userId, normalized, actorId, auditComment, cancellationToken);
    }

    private async Task RequireUserPermissionAsync(
        int actorId, bool delete, CancellationToken cancellationToken) {
        if (await repository.UserExistsAsync(actorId, cancellationToken)) {
            var access = await repository.GetByUserIdAsync(actorId, cancellationToken);
            if (access.Any(item => item.Resource == "Users" &&
                                   (delete ? item.CanDelete : item.CanUpdate)))
                return;
        }
        throw new UnauthorizedAccessException("User management permission is required.");
    }
}
