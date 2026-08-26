namespace Webboard.Web.Authentication;

public static class HostAccess {
    public const string Read = "Hosts:read";
    public const string Create = "Hosts:create";
    public const string Update = "Hosts:update";
    public const string Delete = "Hosts:delete";

    public static bool Has(System.Security.Claims.ClaimsPrincipal user, string access) =>
        user.HasClaim(JwtSessionService.AccessClaimType, access);
}
