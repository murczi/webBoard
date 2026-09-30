namespace Webboard.Domain.Interfaces.Services;
using Model.Monitoring;
public interface IMonitoringReader {
    Task<IReadOnlyList<MonitoringStatus>> LatestAsync(CancellationToken token);
    Task<IReadOnlyList<HistoryModule>> ModulesAsync(CancellationToken token);
    Task<MonitoringBlock> BlockAsync(int moduleId, DateTimeOffset at, CancellationToken token);
    Task<HistorySlice> HistoryAsync(int moduleId, DateTimeOffset from, DateTimeOffset until, CancellationToken token);
}
public interface IMonitoringCollector {
    Task<IReadOnlyList<int>> EnabledModuleIdsAsync(CancellationToken token);
    Task CollectAsync(int moduleId, CancellationToken token);
    Task RetainAsync(CancellationToken token);
}
