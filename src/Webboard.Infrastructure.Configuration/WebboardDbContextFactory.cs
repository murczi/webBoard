namespace Webboard.Infrastructure.Configuration;

using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public sealed class WebboardDbContextFactory
    : IDesignTimeDbContextFactory<WebboardDbContext> {
    public WebboardDbContext CreateDbContext(string[] args) {
        Env.TraversePath().Load();

        var connectionString =
            Environment.GetEnvironmentVariable(
            "ConnectionStrings__WebboardDatabase");

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
            "Environment variable " +
            "'ConnectionStrings__WebboardDatabase' is not configured.");

        var options =
            new DbContextOptionsBuilder<WebboardDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        return new WebboardDbContext(options);
    }
}
