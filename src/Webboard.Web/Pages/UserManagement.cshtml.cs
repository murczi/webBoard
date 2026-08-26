namespace Webboard.Web.Pages;

using System.Security.Claims;
using Authentication;
using Domain.Interfaces.Services;
using Domain.Model.AuditLogs;
using Domain.Model.UserCrudAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Authorize]
public class UserManagementModel(
    IUserCrudAccessService userCrudAccessService,
    IAuditLogService auditLogs) : PageModel {
    public IReadOnlyList<UserSummaryModel> Users { get; private set; }
        = Array.Empty<UserSummaryModel>();

    public int? CurrentUserId {
        get {
            var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(value, out var userId) ? userId : null;
        }
    }

    public bool CanReadLogs => AuditLogAccess.HasRead(User);

    [BindProperty]
    public List<UserCrudAccessModel> Access { get; set; } = [];

    [BindProperty]
    public string AuditComment { get; set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken) {
        Users = await userCrudAccessService.GetUsersAsync(cancellationToken);
    }

    public async Task<IActionResult> OnGetAccessAsync(
        int userId,
        CancellationToken cancellationToken) {
        try {
            return new JsonResult(
                await userCrudAccessService.GetAccessAsync(userId, cancellationToken));
        }
        catch (KeyNotFoundException) {
            return NotFound();
        }
    }

    public async Task<IActionResult> OnPostSaveAccessAsync(
        int userId,
        CancellationToken cancellationToken) {
        if (CurrentUserId is not int actorId)
            return Challenge();
        if (!IsValidAuditComment()) {
            TempData["ErrorMessage"] =
                $"Enter an audit comment of no more than {AuditLogModel.MaxCommentLength} characters.";
            return RedirectToPage();
        }

        try {
            await userCrudAccessService.SaveAccessAsync(
                userId,
                Access,
                actorId,
                AuditComment,
                cancellationToken);
            TempData["StatusMessage"] = "CRUD access updated.";
        }
        catch (KeyNotFoundException) {
            TempData["ErrorMessage"] = "The selected user no longer exists.";
        }
        catch (ArgumentException) {
            TempData["ErrorMessage"] = "The submitted access settings were invalid.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(
        int userId,
        CancellationToken cancellationToken) {
        if (CurrentUserId == userId) {
            TempData["ErrorMessage"] = "You cannot delete your own account.";
            return RedirectToPage();
        }
        if (CurrentUserId is not int actorId)
            return Challenge();

        var deleted = await userCrudAccessService.DeleteUserAsync(
            userId,
            actorId,
            "Deleted user.",
            cancellationToken);
        TempData[deleted ? "StatusMessage" : "ErrorMessage"] = deleted
            ? "User deleted."
            : "The selected user no longer exists.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetLogsAsync(
        int userId,
        int page = 1,
        CancellationToken cancellationToken = default) {
        if (!CanReadLogs)
            return Forbid();

        return new JsonResult(await auditLogs.GetUserLogsAsync(userId, page, cancellationToken));
    }

    private bool IsValidAuditComment() =>
        !string.IsNullOrWhiteSpace(AuditComment) &&
        AuditComment.Trim().Length <= AuditLogModel.MaxCommentLength;
}
