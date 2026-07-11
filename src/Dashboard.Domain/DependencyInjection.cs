using Dashboard.Domain.Overview.Interfaces;
using Dashboard.Domain.Overview.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Dashboard.Domain;

public static class DependencyInjection
{
    public static IServiceCollection AddDomain(
        this IServiceCollection services)
    {
        services.AddScoped<
            IDashboardOverviewService,
            DashboardOverviewService>();

        return services;
    }
}