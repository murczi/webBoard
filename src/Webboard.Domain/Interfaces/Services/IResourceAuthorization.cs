namespace Webboard.Domain.Interfaces.Services;

using Model.UserCrudAccess;

public interface IResourceAuthorization {
    Task<IReadOnlyList<UserCrudAccessModel>?> GetCurrentAsync(int actorId, CancellationToken token);
    Task RequireAsync(int actorId, string resource, string action, CancellationToken token);
}
