using Dashboard.Domain.Overview.Models;

namespace Dashboard.Domain.Overview.Interfaces;

public interface IDashboardOverviewService
{
    Task<IReadOnlyList<ModuleTileModel>> GetTilesAsync(
        CancellationToken cancellationToken = default);
}