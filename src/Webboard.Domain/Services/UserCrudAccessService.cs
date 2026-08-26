namespace Webboard.Domain.Services;

using Interfaces.Repositories;
using Interfaces.Services;
using Model.UserCrudAccess;

public class UserCrudAccessService(IUserCrudAccessRepository repository)
    : IUserCrudAccessService {
    private static readonly IReadOnlyDictionary<string, bool> Resources =
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["Users"] = false,
            ["Hosts"] = false,
            ["Modules"] = false,
            ["ModuleTypes"] = true,
            ["AuditLogs"] = true
        };

    public Task<IReadOnlyList<UserSummaryModel>> GetUsersAsync(
        CancellationToken cancellationToken = default) =>
        repository.GetUsersAsync(cancellationToken);

    public Task<bool> DeleteUserAsync(
        int userId,
        CancellationToken cancellationToken = default) =>
        repository.DeleteUserAsync(userId, cancellationToken);

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
        CancellationToken cancellationToken = default) {
        if (!await repository.UserExistsAsync(userId, cancellationToken))
            throw new KeyNotFoundException($"User {userId} was not found.");

        var submitted = access.ToDictionary(item => item.Resource, StringComparer.Ordinal);
        if (submitted.Keys.Any(resource => !Resources.ContainsKey(resource)))
            throw new ArgumentException("An unknown CRUD resource was submitted.", nameof(access));

        var existing = (await repository.GetByUserIdAsync(userId, cancellationToken))
            .ToDictionary(item => item.Resource, StringComparer.Ordinal);

        foreach (var resource in Resources) {
            if (!submitted.TryGetValue(resource.Key, out var item)) {
                if (existing.TryGetValue(resource.Key, out var removed))
                    repository.Delete(removed);
                continue;
            }

            item.UserId = userId;
            item.IsReadOnlyResource = resource.Value;
            if (resource.Value) {
                item.CanCreate = false;
                item.CanUpdate = false;
                item.CanDelete = false;
            }

            if (existing.TryGetValue(resource.Key, out var current)) {
                item.Id = current.Id;
                repository.Update(item);
            }
            else {
                await repository.AddAsync(item, cancellationToken);
            }
        }

        await repository.SaveChangesAsync(cancellationToken);
    }
}
