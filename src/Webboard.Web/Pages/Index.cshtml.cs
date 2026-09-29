namespace Webboard.Web.Pages;

using System.Security.Claims;
using Authentication;
using Domain.Interfaces.Services;
using Domain.Model.Modules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Authorize]
public class IndexModel(
    IModuleManagementService modules,
    IModuleHealthChecker healthChecker,
    IAuditLogService auditLogs,
    JwtSessionService sessions) : PageModel {
    public IReadOnlyList<ModuleTileModel> ModuleTiles { get; private set; } = [];
    public bool CanRead => ModuleAccess.Has(User, ModuleAccess.Read);
    public bool CanCreate => ModuleAccess.Has(User, ModuleAccess.Create);
    public bool CanUpdate => ModuleAccess.Has(User, ModuleAccess.Update);
    public bool CanDelete => ModuleAccess.Has(User, ModuleAccess.Delete);
    public bool CanReadLogs => AuditLogAccess.HasRead(User);

    public async Task OnGetAsync(CancellationToken cancellationToken) {
        var allModules = CanRead
            ? await modules.GetAllAsync(cancellationToken)
            : [];

        if (CanRead) {
            var enabled = allModules.Where(module => module.IsEnabled).ToList();
            var checks = enabled.Select(async module => new ModuleTileModel(
                module,
                await healthChecker.CheckAsync(module, cancellationToken)));
            ModuleTiles = await Task.WhenAll(checks);
        }


    }

    public async Task<IActionResult> OnPostRefreshAccessAsync(
        CancellationToken cancellationToken) {
        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
            return Challenge();

        var rememberMe = User.HasClaim(
            JwtSessionService.RememberMeClaimType,
            bool.TrueString.ToLowerInvariant());
        var token = await sessions.RefreshTokenAsync(
            userName,
            rememberMe,
            cancellationToken);
        if (token is null) {
            Response.Cookies.Delete(JwtOptions.CookieName, new CookieOptions { Path = "/" });
            return RedirectToPage("/Login");
        }

        Response.Cookies.Append(
            JwtOptions.CookieName,
            token,
            sessions.CreateCookieOptions(rememberMe, Request.IsHttps));
        TempData["StatusMessage"] = "Access refreshed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetLogsAsync(
        int moduleId,
        int page = 1,
        CancellationToken cancellationToken = default) {
        if (!CanReadLogs)
            return Forbid();

        return new JsonResult(
            await auditLogs.GetModuleLogsAsync(moduleId, page, cancellationToken));
    }

    public sealed record ModuleTileModel(ModuleModel Module, ModuleHealthResult Health);
}
