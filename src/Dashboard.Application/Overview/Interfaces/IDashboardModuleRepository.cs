using Dashboard.Domain.Entities;

namespace Dashboard.Application.Overview.Interfaces;

public interface IDashboardModuleRepository
{
    Task<IReadOnlyList<DashboardModule>> GetOverviewModulesAsync(
        CancellationToken cancellationToken = default);
}