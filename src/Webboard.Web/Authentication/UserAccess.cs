namespace Webboard.Web.Authentication;

public static class UserAccess {
    public const string Read = "Users:read";
    public const string Update = "Users:update";
    public const string Delete = "Users:delete";

    public static bool Has(System.Security.Claims.ClaimsPrincipal user, string access) =>
        user.HasClaim(JwtSessionService.AccessClaimType, access);
}
