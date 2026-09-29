namespace Webboard.Web.Pages;

using Authentication;
using Domain.Interfaces.Services;
using Domain.Model.AuditLogs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Authorize]
public class AuditLogsModel(IAuditLogService logs) : PageModel {
    [BindProperty(SupportsGet = true)] public string? Resource { get; set; }
    [BindProperty(SupportsGet = true)] public AuditAction? Action { get; set; }
    [BindProperty(SupportsGet = true)] public int? ActorId { get; set; }
    [BindProperty(SupportsGet = true)] public int? TargetId { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    public PagedAuditLogsModel Results { get; private set; } = new() { Items = [], Page = 1, PageSize = 25 };
    public IReadOnlyList<AuditActorModel> Actors { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) {
        if (!AuditLogAccess.HasRead(User)) return Forbid();
        Actors = await logs.GetActorsAsync(cancellationToken);
        if (!ModelState.IsValid) return Page();
        if (To?.Date == DateTime.MaxValue.Date) {
            ModelState.AddModelError(nameof(To), "Choose an end date before 9999-12-31.");
            return Page();
        }
        try {
            Results = await logs.SearchAsync(new AuditLogQuery {
                Resource = string.IsNullOrWhiteSpace(Resource) ? null : Resource,
                Action = Action, ActorId = ActorId, TargetId = TargetId, Page = PageNumber,
                FromUtc = From.HasValue ? DateTime.SpecifyKind(From.Value.Date, DateTimeKind.Utc) : null,
                UntilUtc = To.HasValue ? DateTime.SpecifyKind(To.Value.Date.AddDays(1), DateTimeKind.Utc) : null
            }, cancellationToken);
        } catch (ArgumentException exception) {
            ModelState.AddModelError(string.Empty, exception.Message);
        }
        return Page();
    }

    public string PageUrl(int page, bool clearTarget = false) => Url.Page("/AuditLogs", new {
        Resource, Action, ActorId, TargetId = clearTarget ? null : TargetId,
        From = From?.ToString("yyyy-MM-dd"), To = To?.ToString("yyyy-MM-dd"), PageNumber = page
    })!;
    public static string ActionLabel(AuditAction action) => action == AuditAction.PermissionsChanged ? "Permissions changed" : action.ToString();
}
