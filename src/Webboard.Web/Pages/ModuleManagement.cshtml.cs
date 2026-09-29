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
public class ModuleManagementModel(
    IModuleManagementService modules,
    IHostManagementService hosts,
    IDockerAgentClient dockerAgent,
    ISystemdAgentClient systemdAgent,
    IAuditLogService auditLogs) : PageModel {
    public IReadOnlyList<ModuleModel> VisibleModules { get; private set; } = [];
    public IReadOnlyList<ModuleModel> HiddenModules { get; private set; } = [];
    public IReadOnlyList<ModuleOptionModel> Hosts { get; private set; } = [];
    public IReadOnlyList<ModuleOptionModel> Types { get; private set; } = [];

    public bool CanRead => ModuleAccess.Has(User, ModuleAccess.Read);
    public string? ReopenHandler { get; private set; }

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

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) {
        if (!CanRead) return Forbid();
        var canManage = CanCreate || CanUpdate || CanDelete;
        var allModules = await modules.GetAllAsync(cancellationToken);
        VisibleModules = allModules.Where(module => module.IsEnabled).ToList();
        if (canManage)
            HiddenModules = allModules.Where(module => !module.IsEnabled).ToList();

        if (CanCreate || CanUpdate) {
            Hosts = await modules.GetHostsAsync(cancellationToken);
            Types = await modules.GetTypesAsync(cancellationToken);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAddAsync(CancellationToken cancellationToken) {
        if (!CanCreate)
            return Forbid();
        if (CurrentUserId is not int actorId)
            return Challenge();
        // Audit comments are edit-only. The add form intentionally disables this field,
        // so it must not participate in model validation for module creation.
        ModelState.Remove(nameof(AuditComment));
        if (!ModelState.IsValid) {
            TempData["ErrorMessage"] = GetModelStateErrorMessage();
            return await RedisplayAsync("Add", cancellationToken);
        }

        try {
            await modules.AddAsync(Module.ToModel(), actorId, "Created module.", cancellationToken);
            TempData["StatusMessage"] = "Module added.";
        }
        catch (ArgumentException exception) {
            TempData["ErrorMessage"] = exception.Message;
        }
        if (TempData.ContainsKey("ErrorMessage")) return await RedisplayAsync("Add", cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(CancellationToken cancellationToken) {
        if (!CanUpdate)
            return Forbid();
        if (CurrentUserId is not int actorId)
            return Challenge();
        if (!IsValidAuditComment() || !ModelState.IsValid) {
            TempData["ErrorMessage"] = GetModelStateErrorMessage();
            return await RedisplayAsync("Update", cancellationToken);
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
        if (TempData.ContainsKey("ErrorMessage")) return await RedisplayAsync("Update", cancellationToken);
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

    public async Task<IActionResult> OnGetContainersAsync(
        int hostId,
        CancellationToken cancellationToken = default) {
        if (!CanCreate && !CanUpdate)
            return Forbid();

        var host = (await hosts.GetAllAsync(cancellationToken))
            .SingleOrDefault(candidate => candidate.Id == hostId && candidate.IsEnabled);
        if (host is null)
            return NotFound(new { error = "The selected host was not found or is disabled." });

        try {
            return new JsonResult(await dockerAgent.GetContainersAsync(
                host.AgentBaseUrl,
                cancellationToken));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return StatusCode(504, new { error = "The Docker container request timed out." });
        }
        catch (HttpRequestException) {
            return StatusCode(502, new { error = "The host agent could not read Docker containers." });
        }
    }

    public async Task<IActionResult> OnGetServicesAsync(
        int hostId,
        CancellationToken cancellationToken = default) {
        if (!CanCreate && !CanUpdate)
            return Forbid();

        var host = (await hosts.GetAllAsync(cancellationToken))
            .SingleOrDefault(candidate => candidate.Id == hostId && candidate.IsEnabled);
        if (host is null)
            return NotFound(new { error = "The selected host was not found or is disabled." });

        try {
            return new JsonResult(await systemdAgent.GetServicesAsync(
                host.AgentBaseUrl,
                cancellationToken));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return StatusCode(504, new { error = "The systemd service request timed out." });
        }
        catch (HttpRequestException exception) {
            return StatusCode(502, new
            {
                error = $"The host agent could not read systemd services: {exception.Message}"
            });
        }
    }

    private async Task<IActionResult> RedisplayAsync(string handler, CancellationToken cancellationToken) {
        ReopenHandler = handler;
        await OnGetAsync(cancellationToken);
        return Page();
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

    private string GetModelStateErrorMessage() {
        foreach (var (field, entry) in ModelState) {
            var error = entry.Errors.FirstOrDefault();
            if (error is null)
                continue;

            var fieldName = field switch {
                nameof(AuditComment) => "Audit comment",
                _ when field.StartsWith("Module.", StringComparison.Ordinal) =>
                    field["Module.".Length..].Replace("Id", "", StringComparison.Ordinal).Trim(),
                _ => field
            };
            var message = string.IsNullOrWhiteSpace(error.ErrorMessage)
                ? $"The submitted value for {fieldName} is invalid."
                : error.ErrorMessage;
            return $"{fieldName}: {message}";
        }

        return "Check the module details and try again.";
    }


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

        [StringLength(128)]
        [Display(Name = "Docker container")]
        public string? ContainerId { get; set; }

        [StringLength(256)]
        [Display(Name = "systemd service")]
        public string? ServiceName { get; set; }

        [StringLength(253)]
        [Display(Name = "Minecraft server address")]
        public string? MinecraftServerAddress { get; set; }

        [Range(1, 65535)]
        [Display(Name = "Minecraft server port")]
        public int? MinecraftServerPort { get; set; }

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
            ContainerId = ContainerId,
            ServiceName = ServiceName,
            MinecraftServerAddress = MinecraftServerAddress,
            MinecraftServerPort = MinecraftServerPort,
            ManagementUrl = ManagementUrl,
            IsEnabled = IsEnabled
        };
    }
}
