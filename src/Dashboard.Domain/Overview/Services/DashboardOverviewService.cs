using Dashboard.Domain.Overview.Interfaces;
using Dashboard.Domain.Overview.Models;
using Dashboard.Domain.Modules;

namespace Dashboard.Domain.Overview.Services;

public sealed class DashboardOverviewService(
    IDashboardModuleRepository moduleRepository)
    : IDashboardOverviewService
{
    public async Task<IReadOnlyList<ModuleTileModel>> GetTilesAsync(
        CancellationToken cancellationToken = default)
    {
        var modules = await moduleRepository.GetOverviewModulesAsync(
            cancellationToken);

        return modules
            .Select(module => new ModuleTileModel
            {
                Id = module.Id,
                Name = module.Name,
                Type = module.Type,
                Health = module.CurrentStatus?.Health
                    ?? ModuleHealth.Unknown,
                IsEnabled = module.IsEnabled,
                ManagementUrl = module.ManagementUrl,
                ManagedHostId = module.ManagedHostId,
                ManagedHostName = module.ManagedHost?.Name,
                SortOrder = module.SortOrder
            })
            .ToList();
    }
}
