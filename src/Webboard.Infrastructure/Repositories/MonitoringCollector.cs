namespace Webboard.Infrastructure.Repositories;
using System.Text.Json;
using Configuration;
using Configuration.Entities;
using Domain.Interfaces.Services;
using Domain.Model.Monitoring;
using Domain.Model.Modules;
using Microsoft.EntityFrameworkCore;
using Npgsql;

public sealed class MonitoringCollector(WebboardDbContext db, IModuleHealthChecker checker, MonitoringOptions options) : IMonitoringCollector {
    public async Task<IReadOnlyList<int>> EnabledModuleIdsAsync(CancellationToken token) =>
        await db.Modules.AsNoTracking().Where(x => x.IsEnabled && !x.DeletionFlag &&
            (x.HostId == null || x.Host != null && x.Host.IsEnabled && !x.Host.DeletionFlag) &&
            !db.LatestMonitoring.Any(latest => latest.ModuleId == x.Id && latest.ConfigurationRevision == x.DateUpdated && latest.Timestamp > DateTimeOffset.UtcNow.AddSeconds(-options.IntervalSeconds))).Select(x => x.Id).ToListAsync(token);
    public async Task CollectAsync(int moduleId, CancellationToken token) {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var acquire = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(70111, @module)", connection);
        acquire.Parameters.AddWithValue("module", moduleId);
        if (!(bool)(await acquire.ExecuteScalarAsync(token))!) return;
        var module = await db.Modules.AsNoTracking().Include(x => x.Type).Include(x => x.Host)
            .SingleOrDefaultAsync(x => x.Id == moduleId, token);
        if (module is null || !module.IsEnabled || module.DeletionFlag || module.HostId.HasValue && (module.Host is null || !module.Host.IsEnabled || module.Host.DeletionFlag)) return;
        var latest = await db.LatestMonitoring.SingleOrDefaultAsync(x => x.ModuleId == moduleId, token);
        var now = DateTimeOffset.UtcNow;
        if (latest is not null && latest.ConfigurationRevision == module.DateUpdated && latest.Timestamp > now.AddSeconds(-options.IntervalSeconds)) return;
        var model = new ModuleModel {
            Id = module.Id, Name = module.FriendlyName, TypeId = module.TypeId, TypeName = module.Type?.Name ?? "",
            HostId = module.HostId, HostName = module.Host?.Name, HostAgentBaseUrl = module.Host?.AgentBaseUrl,
            HealthCheckUrl = module.HealthCheckUrl, ContainerId = module.ContainerId, ServiceName = module.ServiceName,
            MinecraftServerAddress = module.MinecraftServerAddress, MinecraftServerPort = module.MinecraftServerPort,
            SteamServerAddress = module.SteamServerAddress, SteamQueryPort = module.SteamQueryPort, SteamQueryPlayers = module.SteamQueryPlayers, IsEnabled = true
        };
        ModuleHealthResult result;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        // All handlers receive the deadline; shutdown never becomes an unhealthy observation.
        try { result = await checker.CheckAsync(model, deadline.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { result = new(ModuleHealthState.Unhealthy, null, "Monitoring check timed out"); }
        catch (Exception) when (!token.IsCancellationRequested) { result = new(ModuleHealthState.Unhealthy, null, "Monitoring check failed"); }
        token.ThrowIfCancellationRequested();
        now = DateTimeOffset.UtcNow;
        var message = result.Message[..Math.Min(result.Message.Length, 1000)];
        db.MonitoringResults.Add(new() { ModuleId = moduleId, Timestamp = now, FreshUntil = now.AddSeconds(options.StaleSeconds),
            ConfigurationRevision = module.DateUpdated, State = result.State, PingMilliseconds = result.PingMilliseconds, Message = message });
        if (latest is null) { latest = new() { ModuleId = moduleId }; db.LatestMonitoring.Add(latest); }
        latest.Timestamp = now; latest.FreshUntil = now.AddSeconds(options.StaleSeconds); latest.ConfigurationRevision = module.DateUpdated;
        latest.State = result.State; latest.PingMilliseconds = result.PingMilliseconds; latest.Message = message;
        latest.Details = result.Steam is null ? null : JsonSerializer.Serialize(result.Steam);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }
    public async Task RetainAsync(CancellationToken token) {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-options.RetentionDays);
        int removed;
        do {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var acquire = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(70112, 0)", connection);
            if (!(bool)(await acquire.ExecuteScalarAsync(token))!) return;
            removed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM "MonitoringResults" WHERE "Id" IN
                  (SELECT "Id" FROM "MonitoringResults" WHERE "Timestamp" < {cutoff} ORDER BY "Timestamp" LIMIT 10000)
                """, token);
            await transaction.CommitAsync(token);
        } while (removed == 10000 && !token.IsCancellationRequested);
    }
}
