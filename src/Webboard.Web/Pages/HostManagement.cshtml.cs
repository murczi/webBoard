namespace Webboard.Web.Pages;

using System.ComponentModel.DataAnnotations;
using Authentication;
using Domain.Interfaces.Services;
using Domain.Model.Hosts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Authorize(Policy = HostAccess.Read)]
public class HostManagementModel(IHostManagementService hosts) : PageModel {
    public IReadOnlyList<HostModel> Hosts { get; private set; } = Array.Empty<HostModel>();

    public bool CanCreate => HostAccess.Has(User, HostAccess.Create);
    public bool CanUpdate => HostAccess.Has(User, HostAccess.Update);
    public bool CanDelete => HostAccess.Has(User, HostAccess.Delete);

    [BindProperty]
    public HostInputModel Host { get; set; } = new();

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
        if (!ModelState.IsValid) {
            SetValidationError();
            return RedirectToPage();
        }

        try {
            await hosts.AddAsync(Host.ToModel(), cancellationToken);
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
        if (!ModelState.IsValid) {
            SetValidationError();
            return RedirectToPage();
        }

        try {
            var updated = await hosts.UpdateAsync(Host.ToModel(), cancellationToken);
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

        var deleted = await hosts.DeleteAsync(hostId, cancellationToken);
        TempData[deleted ? "StatusMessage" : "ErrorMessage"] = deleted
            ? "Host deleted."
            : "The selected host no longer exists.";
        return RedirectToPage();
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
