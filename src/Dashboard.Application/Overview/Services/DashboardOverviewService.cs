using Dashboard.Application.Overview.DTOs;
using Dashboard.Application.Overview.Interfaces;
using Dashboard.Domain.Modules;

namespace Dashboard.Application.Overview.Services;

public sealed class DashboardOverviewService(
    IDashboardModuleRepository moduleRepository)
    : IDashboardOverviewService
{
    public async Task<IReadOnlyList<ModuleTileDto>> GetTilesAsync(CancellationToken cancellationToken = default)
    {
        var modules = await moduleRepository.GetOverviewModulesAsync(cancellationToken);

        return modules.Select(module => new ModuleTileDto(
            module.Id,
            module.Name,
            module.Type,
            module.CurrentStatus?.Health
            ?? ModuleHealth.Unknown,
            module.IsEnabled,
            module.ManagementUrl,
            module.ManagedHostId,
            module.ManagedHost?.Name,
            module.SortOrder)).ToList();
    }
}