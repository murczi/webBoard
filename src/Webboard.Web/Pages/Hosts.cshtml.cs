namespace Webboard.Web.Pages;

using Authentication;
using Domain.Interfaces.Services;
using Domain.Model.Hosts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

[Authorize(Policy = HostAccess.Read)]
public class HostsModel(IHostManagementService hosts) : PageModel {
    public IReadOnlyList<HostModel> Hosts { get; private set; } = [];
    public bool CanManage => HostAccess.Has(User, HostAccess.Create) || HostAccess.Has(User, HostAccess.Update) || HostAccess.Has(User, HostAccess.Delete);
    public bool CanReadLogs => AuditLogAccess.HasRead(User);
    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Hosts = await hosts.GetAllAsync(cancellationToken);
}
