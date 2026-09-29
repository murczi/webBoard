namespace Webboard.Web.Pages;

using System.Security.Claims;
using Authentication;
using Domain.Interfaces.Services;
using Domain.Model.AuditLogs;
using Domain.Model.UserCrudAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Authorize(Policy = UserAccess.Read)]
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

    public int? RetryUserId { get; private set; }
    public string? RetryUserName { get; private set; }

    public bool CanUpdateUsers => UserAccess.Has(User, UserAccess.Update);
    public bool CanDeleteUsers => UserAccess.Has(User, UserAccess.Delete);

    public bool CanReadLogs => AuditLogAccess.HasRead(User);

    [BindProperty]
    public List<UserCrudAccessModel> Access { get; set; } = [];

    [BindProperty]
    public string AuditComment { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) {
        if (!CanUpdateUsers && !CanDeleteUsers) return RedirectToPage("/Users");
        Users = await userCrudAccessService.GetUsersAsync(cancellationToken);
        return Page();
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
        if (!CanUpdateUsers)
            return Forbid();
        if (CurrentUserId is not int actorId)
            return Challenge();
        if (!ModelState.IsValid || !IsValidAuditComment()) {
            TempData["ErrorMessage"] =
                $"Enter an audit comment of no more than {AuditLogModel.MaxCommentLength} characters.";
            return await RedisplayAccessAsync(userId, cancellationToken);
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
        catch (UnauthorizedAccessException) {
            return Forbid();
        }
        catch (KeyNotFoundException) {
            TempData["ErrorMessage"] = "The selected user no longer exists.";
        }
        catch (ArgumentException) {
            TempData["ErrorMessage"] = "The submitted access settings were invalid.";
        }

        if (TempData.ContainsKey("ErrorMessage")) return await RedisplayAccessAsync(userId, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(
        int userId,
        CancellationToken cancellationToken) {
        if (!CanDeleteUsers)
            return Forbid();
        if (CurrentUserId == userId) {
            TempData["ErrorMessage"] = "You cannot delete your own account.";
            return RedirectToPage();
        }
        if (CurrentUserId is not int actorId)
            return Challenge();

        try {
            var deleted = await userCrudAccessService.DeleteUserAsync(
                userId,
                actorId,
                "Deleted user.",
                cancellationToken);
            TempData[deleted ? "StatusMessage" : "ErrorMessage"] = deleted
                ? "User deleted."
                : "The selected user no longer exists.";
        }
        catch (UnauthorizedAccessException) {
            return Forbid();
        }

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

    private async Task<IActionResult> RedisplayAccessAsync(int userId, CancellationToken cancellationToken) {
        Users = await userCrudAccessService.GetUsersAsync(cancellationToken);
        RetryUserName = Users.FirstOrDefault(user => user.Id == userId)?.Name;
        RetryUserId = RetryUserName is null ? null : userId;
        foreach (var item in Access)
            item.IsReadOnlyResource = item.Resource is "AuditLogs" or "ModuleTypes";
        return Page();
    }

    private bool IsValidAuditComment() =>
        !string.IsNullOrWhiteSpace(AuditComment) &&
        AuditComment.Trim().Length <= AuditLogModel.MaxCommentLength;
}
