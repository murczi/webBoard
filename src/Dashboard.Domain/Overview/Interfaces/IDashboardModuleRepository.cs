using Dashboard.Domain.Entities;

namespace Dashboard.Domain.Overview.Interfaces;

public interface IDashboardModuleRepository
{
    Task<IReadOnlyList<DashboardModule>> GetOverviewModulesAsync(
        CancellationToken cancellationToken = default);
}