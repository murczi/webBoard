namespace Webboard.Web.Authentication;

public static class ModuleAccess {
    public const string Read = "Modules:read";
    public const string Create = "Modules:create";
    public const string Update = "Modules:update";
    public const string Delete = "Modules:delete";

    public static bool Has(System.Security.Claims.ClaimsPrincipal user, string access) =>
        user.HasClaim(JwtSessionService.AccessClaimType, access);
}
