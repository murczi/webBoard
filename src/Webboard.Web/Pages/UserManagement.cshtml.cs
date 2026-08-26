namespace Webboard.Web.Pages;

using System.Security.Claims;
using Domain.Interfaces.Services;
using Domain.Model.UserCrudAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Authorize]
public class UserManagementModel(IUserCrudAccessService userCrudAccessService) : PageModel {
    public IReadOnlyList<UserSummaryModel> Users { get; private set; }
        = Array.Empty<UserSummaryModel>();

    public int? CurrentUserId {
        get {
            var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(value, out var userId) ? userId : null;
        }
    }

    [BindProperty]
    public List<UserCrudAccessModel> Access { get; set; } = [];

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
        try {
            await userCrudAccessService.SaveAccessAsync(
                userId,
                Access,
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

        var deleted = await userCrudAccessService.DeleteUserAsync(
            userId,
            cancellationToken);
        TempData[deleted ? "StatusMessage" : "ErrorMessage"] = deleted
            ? "User deleted."
            : "The selected user no longer exists.";

        return RedirectToPage();
    }
}
