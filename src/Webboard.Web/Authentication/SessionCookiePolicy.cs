namespace Webboard.Web.Authentication;

public static class SessionCookiePolicy {
    public static bool RequiresSecure(HttpContext context) =>
        context.Request.IsHttps || !context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment();
}
