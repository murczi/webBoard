namespace Webboard.Web.Pages;

using Authentication;
using Domain.Interfaces.Services;
using Domain.Model.UserCrudAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

[Authorize(Policy = UserAccess.Read)]
public class UsersModel(IUserCrudAccessService users) : PageModel {
    public IReadOnlyList<UserSummaryModel> Users { get; private set; } = [];
    public IReadOnlyList<UserCrudAccessModel> Permissions { get; private set; } = [];
    public string? SelectedName { get; private set; }
    public int? CurrentUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public bool CanManage => UserAccess.Has(User, UserAccess.Update) || UserAccess.Has(User, UserAccess.Delete);
    public bool CanReadLogs => AuditLogAccess.HasRead(User);
    public async Task<IActionResult> OnGetAsync(int? userId, CancellationToken cancellationToken) {
        Users = await users.GetUsersAsync(cancellationToken);
        if (userId.HasValue) {
            SelectedName = Users.FirstOrDefault(u => u.Id == userId)?.Name;
            if (SelectedName is null) return NotFound();
            Permissions = await users.GetAccessAsync(userId.Value, cancellationToken);
        }
        return Page();
    }
}
