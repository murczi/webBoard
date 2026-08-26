namespace Webboard.Web.Authentication;

public static class AuditLogAccess {
    public const string Read = "AuditLogs:read";

    public static bool HasRead(System.Security.Claims.ClaimsPrincipal user) =>
        user.HasClaim(JwtSessionService.AccessClaimType, Read);
}
