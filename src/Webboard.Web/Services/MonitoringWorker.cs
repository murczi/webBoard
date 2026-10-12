namespace Webboard.Web.Services;
using Domain.Interfaces.Services;
using Domain.Model.Monitoring;

public sealed class MonitoringWorker(IServiceScopeFactory scopes, MonitoringOptions options, ILogger<MonitoringWorker> logger) : BackgroundService {
    private readonly TimeProvider timeProvider = TimeProvider.System;
    public MonitoringWorker(IServiceScopeFactory scopes, MonitoringOptions options, ILogger<MonitoringWorker> logger, TimeProvider timeProvider) : this(scopes, options, logger) => this.timeProvider = timeProvider;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (!options.Enabled) return;
        var cleanupAt = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested) {
            try {
                await using var scope = scopes.CreateAsyncScope();
                var collector = scope.ServiceProvider.GetRequiredService<IMonitoringCollector>();
                var modules = await collector.EnabledModuleIdsAsync(stoppingToken);
                await Parallel.ForEachAsync(modules, new ParallelOptions { MaxDegreeOfParallelism = options.MaxConcurrency, CancellationToken = stoppingToken }, async (id, token) => {
                    try {
                        await using var checkScope = scopes.CreateAsyncScope();
                        await checkScope.ServiceProvider.GetRequiredService<IMonitoringCollector>().CollectAsync(id, token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                    catch (Exception exception) { logger.LogWarning("Monitoring persistence failed for module {ModuleId}: {Diagnostics}", id, ExceptionDiagnostics.Describe(exception)); }
                });
                if (timeProvider.GetUtcNow() >= cleanupAt) {
                    await collector.RetainAsync(stoppingToken);
                    cleanupAt = timeProvider.GetUtcNow().AddHours(1);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning("Monitoring worker will retry: {Diagnostics}", ExceptionDiagnostics.Describe(exception)); }
            try { await Task.Delay(TimeSpan.FromSeconds(1), timeProvider, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
