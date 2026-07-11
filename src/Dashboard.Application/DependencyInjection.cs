using Dashboard.Application.Overview.Interfaces;
using Dashboard.Application.Overview.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Dashboard.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services)
    {
        services.AddScoped<
            IDashboardOverviewService,
            DashboardOverviewService>();

        return services;
    }
}