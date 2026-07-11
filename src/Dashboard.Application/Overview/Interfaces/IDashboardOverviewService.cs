using Dashboard.Application.Overview.DTOs;

namespace Dashboard.Application.Overview.Interfaces;

public interface IDashboardOverviewService
{
    Task<IReadOnlyList<ModuleTileDto>> GetTilesAsync(
        CancellationToken cancellationToken = default);
}