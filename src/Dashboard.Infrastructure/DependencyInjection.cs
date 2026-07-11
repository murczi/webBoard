using Dashboard.Domain.Overview.Interfaces;
using Dashboard.Infrastructure.Persistence;
using Dashboard.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dashboard.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString =
            configuration.GetConnectionString(
                "DashboardDatabase");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'DashboardDatabase' is not configured.");
        }

        services.AddDbContext<DashboardDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<
            IDashboardModuleRepository,
            DashboardModuleRepository>();

        return services;
    }
}