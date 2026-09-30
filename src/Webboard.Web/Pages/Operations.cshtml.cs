namespace Webboard.Web.Pages;
using System.Security.Claims;
using Authentication;
using Domain.Interfaces.Services;
using Domain.Model.Modules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Authorize]
public sealed class OperationsModel(IModuleManagementService modules, IModuleOperations operations,
    IHostApplicationLifetime lifetime) : PageModel {
    public ModuleModel Module { get; private set; } = null!;
    public IReadOnlyList<string> AllowedOperations { get; private set; } = [];
    private int Actor => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public async Task<IActionResult> OnGetAsync(int moduleId, CancellationToken token) {
        if (!ModuleAccess.Has(User, ModuleAccess.Read) || !ModuleAccess.Has(User, ModuleAccess.Operations)) return Forbid();
        var module = (await modules.GetAllAsync(token)).SingleOrDefault(x => x.Id == moduleId);
        if (module is null || !module.IsEnabled || module.TypeName is not ("Docker" or "Systemd")) return NotFound();
        Module = module;
        AllowedOperations = await operations.AllowedServiceOperationsAsync(module, token);
        return Page();
    }
    public async Task<IActionResult> OnPostExecuteAsync(int moduleId, string operation, Guid requestId) {
        try { return new JsonResult(await operations.ExecuteAsync(Actor, new(requestId, moduleId, operation), lifetime.ApplicationStopping)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
    }
}
