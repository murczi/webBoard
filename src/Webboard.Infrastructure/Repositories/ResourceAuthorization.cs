namespace Webboard.Infrastructure.Repositories;

using Domain.Interfaces.Repositories;
using Domain.Interfaces.Services;
using Domain.Model.UserCrudAccess;

public sealed class ResourceAuthorization(IUserCrudAccessRepository users) : IResourceAuthorization {
    public async Task<IReadOnlyList<UserCrudAccessModel>?> GetCurrentAsync(int actorId, CancellationToken token) =>
        await users.UserExistsAsync(actorId, token) ? await users.GetByUserIdAsync(actorId, token) : null;

    public async Task RequireAsync(int actorId, string resource, string action, CancellationToken token) {
        var grants = await GetCurrentAsync(actorId, token);
        var grant = grants?.SingleOrDefault(x => x.Resource == resource);
        var allowed = grant is not null && PermissionResources.ActionsFor(grant).Contains(action, StringComparer.Ordinal);
        if (!allowed) throw new UnauthorizedAccessException("Current resource permission is required.");
    }
}
