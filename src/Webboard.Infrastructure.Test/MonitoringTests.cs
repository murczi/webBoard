namespace Webboard.Infrastructure.Test;
using Configuration;
using Configuration.Entities;
using Domain.Interfaces.Services;
using Domain.Model.Modules;
using Domain.Model.Monitoring;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Repositories;

public sealed class MonitoringTests : IAsyncLifetime {
    private readonly string name = "webboard_monitoring_" + Guid.NewGuid().ToString("N");
    private string connectionString = "";
    private int moduleId, actorId, hostId;
    private readonly MonitoringOptions options = new();
    public async Task InitializeAsync() {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("WEBBOARD_TEST_DATABASE"));
        await using var connection = new NpgsqlConnection(builder.ConnectionString); await connection.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE {name}", connection).ExecuteNonQueryAsync();
        builder.Database = name; connectionString = builder.ConnectionString;
        await using var db = Context(); await db.Database.MigrateAsync();
        await new UserAuthenticationRepository(db).CreateAsync("admin", "unused");
        actorId = (await db.Users.SingleAsync()).Id;
        var host = new HostEntity { Name = "fake-host", AgentBaseUrl = "https://fake.test", DateCreated = DateTime.UtcNow, IsEnabled = true };
        db.Hosts.Add(host); await db.SaveChangesAsync(); hostId = host.Id;
        var module = new ModuleEntity { FriendlyName = "sample", TypeId = 2, HostId = host.Id, ContainerId = "fake", DateCreated = DateTimeOffset.UtcNow, DateUpdated = DateTimeOffset.UtcNow, IsEnabled = true };
        db.Modules.Add(module); await db.SaveChangesAsync(); moduleId = module.Id;
    }
    public async Task DisposeAsync() {
        await using var connection = new NpgsqlConnection(Environment.GetEnvironmentVariable("WEBBOARD_TEST_DATABASE")); await connection.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", connection).ExecuteNonQueryAsync();
    }
    private WebboardDbContext Context() => new(new DbContextOptionsBuilder<WebboardDbContext>().UseNpgsql(connectionString).Options);
    [PostgresFact]
    public async Task CollectionPersistsAndDashboardUsesLatestWithoutExecutingChecks() {
        await using var db = Context(); var checker = new FakeChecker();
        var collector = new MonitoringCollector(db, checker, options);
        await collector.CollectAsync(moduleId, default); await collector.CollectAsync(moduleId, default);
        Assert.Equal(1, checker.Calls); Assert.Equal(1, await db.MonitoringResults.CountAsync());
        var status = Assert.Single(await new MonitoringReader(db, options).LatestAsync(default));
        Assert.Equal(ModuleHealthState.Healthy, status.Health.State); Assert.False(status.IsStale);
        await db.LatestMonitoring.ExecuteUpdateAsync(s => s.SetProperty(x => x.FreshUntil, DateTimeOffset.UtcNow.AddMinutes(-1)));
        status = Assert.Single(await new MonitoringReader(db, options).LatestAsync(default));
        Assert.True(status.IsStale); Assert.Equal(ModuleHealthState.Unknown, status.Health.State);
    }
    [PostgresFact]
    public async Task ConcurrentCollectorsDoNotDuplicateChecks() {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checker = new FakeChecker(async token => { entered.SetResult(); await release.Task.WaitAsync(token); });
        await using var first = Context(); await using var second = Context();
        var running = new MonitoringCollector(first, checker, options).CollectAsync(moduleId, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await new MonitoringCollector(second, checker, options).CollectAsync(moduleId, default);
        release.SetResult(); await running;
        Assert.Equal(1, checker.Calls); Assert.Equal(1, await second.MonitoringResults.CountAsync());
    }
    [PostgresFact]
    public async Task DisabledModulesAreNotCollectedAndRetentionKeepsLatest() {
        await using var db = Context(); var checker = new FakeChecker();
        await db.Modules.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsEnabled, false));
        var collector = new MonitoringCollector(db, checker, options);
        Assert.Empty(await collector.EnabledModuleIdsAsync(default)); await collector.CollectAsync(moduleId, default); Assert.Equal(0, checker.Calls);
        await db.Modules.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsEnabled, true));
        await collector.CollectAsync(moduleId, default);
        await db.MonitoringResults.ExecuteUpdateAsync(s => s.SetProperty(x => x.Timestamp, DateTimeOffset.UtcNow.AddDays(-31)));
        await collector.RetainAsync(default);
        Assert.Empty(await db.MonitoringResults.ToListAsync()); Assert.Single(await db.LatestMonitoring.ToListAsync());
    }
    [PostgresFact]
    public async Task HistoryUsesPredecessorMergesStatesAndPreservesMissingPeriods() {
        await using var db = Context(); var start = DateTimeOffset.UtcNow.AddHours(-1);
        db.MonitoringResults.AddRange(
            Point(start.AddSeconds(-20), start.AddSeconds(70), ModuleHealthState.Healthy, "OK"),
            Point(start.AddSeconds(30), start.AddSeconds(120), ModuleHealthState.Healthy, "OK"),
            Point(start.AddSeconds(200), start.AddSeconds(290), ModuleHealthState.Unhealthy, "Timeout"));
        await db.SaveChangesAsync();
        var reader = new MonitoringReader(db, options);
        var history = await reader.HistoryAsync(moduleId, start, start.AddSeconds(300), default);
        Assert.Equal(new[] { ModuleHealthState.Healthy, ModuleHealthState.Unknown, ModuleHealthState.Unhealthy, ModuleHealthState.Unknown }, history.Blocks.Select(x => x.State));
        Assert.Equal(120, (history.Blocks[0].End - history.Blocks[0].Start).TotalSeconds, precision: 4);
        Assert.Null(history.Next);
        var block = await reader.BlockAsync(moduleId, start.AddSeconds(150), default); Assert.Equal(ModuleHealthState.Unknown, block.State);
    }
    [PostgresFact]
    public async Task ConfigurationEditInvalidatesLatestAndCutsHistory() {
        await using var db = Context();
        await new MonitoringCollector(db, new FakeChecker(), options).CollectAsync(moduleId, default);
        db.ChangeTracker.Clear();
        var repository = new ModuleRepository(db);
        var module = Assert.Single(await repository.GetAllAsync());
        module.IsEnabled = false; module.DateUpdated = DateTimeOffset.UtcNow;
        Assert.True(await repository.UpdateAsync(module, actorId, "Disabled", default));
        Assert.Empty(await db.LatestMonitoring.ToListAsync());
        Assert.Equal(ModuleHealthState.Unknown, (await db.MonitoringResults.OrderByDescending(x => x.Id).FirstAsync()).State);
    }
    [PostgresFact]
    public async Task CheckTimeoutIsRecordedButShutdownIsNotAnOutage() {
        await using var db = Context();
        var wait = new FakeChecker(token => Task.Delay(Timeout.InfiniteTimeSpan, token));
        var collector = new MonitoringCollector(db, wait, new MonitoringOptions { TimeoutSeconds = 1 });
        await collector.CollectAsync(moduleId, default);
        Assert.Equal(ModuleHealthState.Unhealthy, (await db.MonitoringResults.SingleAsync()).State);
        await db.LatestMonitoring.ExecuteDeleteAsync(); await db.MonitoringResults.ExecuteDeleteAsync(); db.ChangeTracker.Clear();
        using var shutdown = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => collector.CollectAsync(moduleId, shutdown.Token));
        Assert.Empty(await db.MonitoringResults.ToListAsync());
    }
    private MonitoringResultEntity Point(DateTimeOffset at, DateTimeOffset until, ModuleHealthState state, string message) =>
        new() { ModuleId = moduleId, Timestamp = at, FreshUntil = until, ConfigurationRevision = at, State = state, Message = message };
    private sealed class FakeChecker(Func<CancellationToken, Task>? wait = null) : IModuleHealthChecker {
        public int Calls;
        public async Task<ModuleHealthResult> CheckAsync(ModuleModel module, CancellationToken cancellationToken = default) {
            Interlocked.Increment(ref Calls); if (wait is not null) await wait(cancellationToken);
            return new(ModuleHealthState.Healthy, 3, "OK");
        }
    }
    private sealed class FakeAgent : IAgentOperationsClient {
        public int Calls;
        public Task<ModuleOperationResult> ExecuteAsync(string baseUrl, Guid requestId, int moduleId, string kind, string target, string operation, CancellationToken token) {
            Calls++; return Task.FromResult(new ModuleOperationResult(true, "Done", 0));
        }
    }
}
