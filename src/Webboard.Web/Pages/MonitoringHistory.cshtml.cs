namespace Webboard.Web.Pages;
using System.Security.Claims;
using Domain.Interfaces.Services;
using Domain.Model.Monitoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
[Authorize]
public sealed class MonitoringHistoryModel(IMonitoringReader monitoring, MonitoringOptions options) : PageModel {
    public IReadOnlyList<HistoryModule> Modules { get; private set; } = [];
    public string Account => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    public int StaleSeconds => options.StaleSeconds;
    public int RetentionDays => options.RetentionDays;
    private bool CanRead => User.HasClaim("access", "MonitoringHistory:read");
    public async Task<IActionResult> OnGetAsync(CancellationToken token) {
        if (!CanRead) return Forbid();
        Modules = await monitoring.ModulesAsync(token);
        return Page();
    }
    public async Task<IActionResult> OnGetDataAsync(int moduleId, DateTimeOffset from, DateTimeOffset until, CancellationToken token) {
        if (!CanRead) return Forbid();
        if (!ModelState.IsValid) return BadRequest();
        Response.Headers.CacheControl = "no-store";
        try { return new JsonResult(await monitoring.HistoryAsync(moduleId, from, until, token)); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (KeyNotFoundException) { return NotFound(); }
    }
    public async Task<IActionResult> OnGetBlockAsync(int moduleId, DateTimeOffset at, CancellationToken token) {
        if (!CanRead) return Forbid();
        if (!ModelState.IsValid) return BadRequest();
        Response.Headers.CacheControl = "no-store";
        try { return new JsonResult(await monitoring.BlockAsync(moduleId, at, token)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }
    public async Task<IActionResult> OnGetLatestAsync(CancellationToken token) {
        if (!CanRead) return Forbid();
        Response.Headers.CacheControl = "no-store";
        return new JsonResult(await monitoring.LatestAsync(token));
    }
}
