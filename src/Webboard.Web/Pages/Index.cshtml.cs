namespace Webboard.Web.Pages;

using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Authentication;
using Domain.Interfaces.Services;
using Domain.Model.AuditLogs;
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
    public IReadOnlyList<ModuleModel> HiddenModules { get; private set; } = [];
    public IReadOnlyList<ModuleOptionModel> Hosts { get; private set; } = [];
    public IReadOnlyList<ModuleOptionModel> Types { get; private set; } = [];

    public bool CanRead => ModuleAccess.Has(User, ModuleAccess.Read);
    public bool CanCreate => ModuleAccess.Has(User, ModuleAccess.Create);
    public bool CanUpdate => ModuleAccess.Has(User, ModuleAccess.Update);
    public bool CanDelete => ModuleAccess.Has(User, ModuleAccess.Delete);
    public bool CanReadLogs => AuditLogAccess.HasRead(User);
    public int? CurrentUserId => int.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

    [BindProperty]
    public ModuleInputModel Module { get; set; } = new();

    [BindProperty]
    public string AuditComment { get; set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken) {
        var canManage = CanUpdate || CanDelete;
        var allModules = CanRead || canManage
            ? await modules.GetAllAsync(cancellationToken)
            : [];

        if (CanRead) {
            var enabled = allModules.Where(module => module.IsEnabled).ToList();
            var checks = enabled.Select(async module => new ModuleTileModel(
                module,
                await healthChecker.CheckAsync(module.HealthCheckUrl, cancellationToken)));
            ModuleTiles = await Task.WhenAll(checks);
        }

        if (canManage)
            HiddenModules = allModules.Where(module => !module.IsEnabled).ToList();

        if (CanCreate || CanUpdate) {
            Hosts = await modules.GetHostsAsync(cancellationToken);
            Types = await modules.GetTypesAsync(cancellationToken);
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
            sessions.CreateCookieOptions(rememberMe));
        TempData["StatusMessage"] = "Access refreshed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddAsync(CancellationToken cancellationToken) {
        if (!CanCreate)
            return Forbid();
        if (CurrentUserId is not int actorId)
            return Challenge();
        if (!ModelState.IsValid) {
            TempData["ErrorMessage"] = "Check the module details and try again.";
            return RedirectToPage();
        }

        try {
            await modules.AddAsync(Module.ToModel(), actorId, "Created module.", cancellationToken);
            TempData["StatusMessage"] = "Module added.";
        }
        catch (ArgumentException exception) {
            TempData["ErrorMessage"] = exception.Message;
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(CancellationToken cancellationToken) {
        if (!CanUpdate)
            return Forbid();
        if (CurrentUserId is not int actorId)
            return Challenge();
        if (!IsValidAuditComment() || !ModelState.IsValid) {
            TempData["ErrorMessage"] = "Check the module details and try again.";
            return RedirectToPage();
        }

        try {
            var updated = await modules.UpdateAsync(
                Module.ToModel(), actorId, AuditComment, cancellationToken);
            TempData[updated ? "StatusMessage" : "ErrorMessage"] = updated
                ? "Module updated."
                : "The selected module no longer exists.";
        }
        catch (ArgumentException exception) {
            TempData["ErrorMessage"] = exception.Message;
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(
        int moduleId,
        CancellationToken cancellationToken) {
        if (!CanDelete)
            return Forbid();
        if (CurrentUserId is not int actorId)
            return Challenge();

        var deleted = await modules.DeleteAsync(
            moduleId, actorId, "Deleted module.", cancellationToken);
        TempData[deleted ? "StatusMessage" : "ErrorMessage"] = deleted
            ? "Module deleted."
            : "The selected module no longer exists.";
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

    private bool IsValidAuditComment() {
        if (!string.IsNullOrWhiteSpace(AuditComment) &&
            AuditComment.Trim().Length <= AuditLogModel.MaxCommentLength)
            return true;

        ModelState.AddModelError(
            nameof(AuditComment),
            $"Enter an audit comment of no more than {AuditLogModel.MaxCommentLength} characters.");
        return false;
    }

    public sealed record ModuleTileModel(ModuleModel Module, ModuleHealthResult Health);

    public class ModuleInputModel {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? Description { get; set; }

        [Display(Name = "Host")]
        public int? HostId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Select a module type.")]
        [Display(Name = "Type")]
        public int TypeId { get; set; }

        [StringLength(2048), Url]
        [Display(Name = "Health-check URL")]
        public string? HealthCheckUrl { get; set; }

        [StringLength(2048), Url]
        [Display(Name = "Management URL")]
        public string? ManagementUrl { get; set; }

        [Display(Name = "Show on dashboard")]
        public bool IsEnabled { get; set; } = true;

        public ModuleModel ToModel() => new()
        {
            Id = Id,
            Name = Name,
            Description = Description,
            HostId = HostId,
            TypeId = TypeId,
            HealthCheckUrl = HealthCheckUrl,
            ManagementUrl = ManagementUrl,
            IsEnabled = IsEnabled
        };
    }
}
