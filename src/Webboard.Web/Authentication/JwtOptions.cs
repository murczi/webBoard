namespace Webboard.Web.Authentication;

public sealed class JwtOptions {
    public const string SectionName = "Authentication:Jwt";
    public const string CookieName = "webboard_session";

    public string Issuer { get; set; } = "Webboard";

    public string Audience { get; set; } = "Webboard";

    public string SigningKey { get; set; } = string.Empty;

    public int LifetimeHours { get; set; } = 8;

    public int RememberMeDays { get; set; } = 30;
}
