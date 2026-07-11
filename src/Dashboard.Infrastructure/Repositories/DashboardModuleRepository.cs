using Dashboard.Application.Overview.Interfaces;
using Dashboard.Domain.Entities;
using Dashboard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dashboard.Infrastructure.Repositories;

public sealed class DashboardModuleRepository(
    DashboardDbContext dbContext)
    : IDashboardModuleRepository
{
    public async Task<IReadOnlyList<DashboardModule>> GetOverviewModulesAsync(
        CancellationToken cancellationToken = default)
    {
        return await dbContext.DashboardModules
            .AsNoTracking()
            .Include(module => module.ManagedHost)
            .Include(module => module.CurrentStatus)
            .Where(module => module.ShowOnOverview)
            .OrderBy(module => module.SortOrder)
            .ThenBy(module => module.Name)
            .ToListAsync(cancellationToken);
    }
}