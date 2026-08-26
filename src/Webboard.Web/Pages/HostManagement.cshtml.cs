namespace Webboard.Web.Pages;

using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Authentication;
using Domain.Interfaces.Services;
using Domain.Model.AuditLogs;
using Domain.Model.Hosts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Authorize(Policy = HostAccess.Read)]
public class HostManagementModel(IHostManagementService hosts, IAuditLogService auditLogs) : PageModel {
    public IReadOnlyList<HostModel> Hosts { get; private set; } = Array.Empty<HostModel>();

    public bool CanCreate => HostAccess.Has(User, HostAccess.Create);
    public bool CanUpdate => HostAccess.Has(User, HostAccess.Update);
    public bool CanDelete => HostAccess.Has(User, HostAccess.Delete);
    public bool CanReadLogs => AuditLogAccess.HasRead(User);
    public int? CurrentUserId => int.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

    [BindProperty]
    public HostInputModel Host { get; set; } = new();

    [BindProperty]
    public string AuditComment { get; set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Hosts = await hosts.GetAllAsync(cancellationToken);

    public async Task<IActionResult> OnPostTestAgentAsync(
        string agentBaseUrl,
        CancellationToken cancellationToken) {
        if (!CanCreate && !CanUpdate)
            return Forbid();

        var result = await hosts.TestAgentAsync(agentBaseUrl ?? string.Empty, cancellationToken);
        return new JsonResult(new { healthy = result.IsHealthy, message = result.Message });
    }

    public async Task<IActionResult> OnPostAddAsync(CancellationToken cancellationToken) {
        if (!CanCreate)
            return Forbid();
        if (CurrentUserId is not int actorId)
            return Challenge();
        // Audit comments are edit-only. The add form disables this field, so it must
        // not participate in model validation when a host is created.
        ModelState.Remove(nameof(AuditComment));
        if (!ModelState.IsValid) {
            SetValidationError();
            return RedirectToPage();
        }

        try {
            await hosts.AddAsync(Host.ToModel(), actorId, "Created host.", cancellationToken);
            TempData["StatusMessage"] = "Host added. Its agent reported healthy.";
        }
        catch (ArgumentException exception) {
            TempData["ErrorMessage"] = exception.Message;
        }
        catch (InvalidOperationException exception) {
            TempData["ErrorMessage"] = $"Host was not added: {exception.Message}";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(CancellationToken cancellationToken) {
        if (!CanUpdate)
            return Forbid();
        if (CurrentUserId is not int actorId)
            return Challenge();
        if (!IsValidAuditComment() || !ModelState.IsValid) {
            SetValidationError();
            return RedirectToPage();
        }

        try {
            var updated = await hosts.UpdateAsync(
                Host.ToModel(), actorId, AuditComment, cancellationToken);
            TempData[updated ? "StatusMessage" : "ErrorMessage"] = updated
                ? "Host configuration updated."
                : "The selected host no longer exists.";
        }
        catch (ArgumentException exception) {
            TempData["ErrorMessage"] = exception.Message;
        }
        catch (InvalidOperationException exception) {
            TempData["ErrorMessage"] = $"Host was not updated: {exception.Message}";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(
        int hostId,
        CancellationToken cancellationToken) {
        if (!CanDelete)
            return Forbid();
        if (CurrentUserId is not int actorId)
            return Challenge();

        var deleted = await hosts.DeleteAsync(
            hostId, actorId, "Deleted host.", cancellationToken);
        TempData[deleted ? "StatusMessage" : "ErrorMessage"] = deleted
            ? "Host deleted."
            : "The selected host no longer exists.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetLogsAsync(
        int hostId,
        int page = 1,
        CancellationToken cancellationToken = default) {
        if (!CanReadLogs)
            return Forbid();

        return new JsonResult(await auditLogs.GetHostLogsAsync(hostId, page, cancellationToken));
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

    private void SetValidationError() =>
        TempData["ErrorMessage"] = "Enter a name and a valid agent base URL.";

    public class HostInputModel {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required, StringLength(2048), Url]
        [Display(Name = "Agent base URL")]
        public string AgentBaseUrl { get; set; } = string.Empty;

        [Display(Name = "Enable this host")]
        public bool IsEnabled { get; set; } = true;

        public HostModel ToModel() => new()
        {
            Id = Id,
            Name = Name,
            AgentBaseUrl = AgentBaseUrl,
            IsEnabled = IsEnabled
        };
    }
}
