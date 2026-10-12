namespace Webboard.Web.Authentication;

using System.Security.Claims;
using Domain.Interfaces.Services;
using Domain.Model.UserCrudAccess;

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
        foreach (var grant in grants)
            foreach (var action in PermissionResources.ActionsFor(grant))
                identity.AddClaim(new(JwtSessionService.AccessClaimType, $"{grant.Resource}:{action}"));
        return true;
    }
}
