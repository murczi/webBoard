namespace Webboard.Web.Authentication;

using System.Security.Claims;
using Domain.Interfaces.Services;

public interface ISessionAccessValidator {
    Task<bool> ValidateAsync(ClaimsPrincipal principal, CancellationToken token);
}

public sealed class SessionAccessValidator(IResourceAuthorization access) : ISessionAccessValidator {
    public async Task<bool> ValidateAsync(ClaimsPrincipal principal, CancellationToken token) {
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || id <= 0)
            return false;
        var grants = await access.GetCurrentAsync(id, token);
        if (grants is null || principal.Identity is not ClaimsIdentity identity) return false;
        foreach (var claim in identity.FindAll(JwtSessionService.AccessClaimType).ToArray()) identity.RemoveClaim(claim);
        foreach (var grant in grants) {
            Add("create", grant.CanCreate); Add("update", grant.CanUpdate); Add("delete", grant.CanDelete);
            Add("operations", grant.CanOperate);
            Add("read", grant.CanRead || grant.CanCreate || grant.CanUpdate || grant.CanDelete || grant.CanOperate);
            void Add(string action, bool allowed) {
                if (allowed) identity.AddClaim(new(JwtSessionService.AccessClaimType, $"{grant.Resource}:{action}"));
            }
        }
        return true;
    }
}
