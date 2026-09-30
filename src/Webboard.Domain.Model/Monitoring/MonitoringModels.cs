namespace Webboard.Domain.Model.Monitoring;
using Modules;
public sealed class MonitoringOptions {
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 30;
    public int TimeoutSeconds { get; set; } = 5;
    public int StaleSeconds { get; set; } = 90;
    public int MaxConcurrency { get; set; } = 8;
    public int RetentionDays { get; set; } = 30;
    public void Validate() {
        if (IntervalSeconds is < 5 or > 86400 || TimeoutSeconds < 1 || TimeoutSeconds > IntervalSeconds ||
            StaleSeconds < IntervalSeconds + TimeoutSeconds || StaleSeconds > 604800 || MaxConcurrency is < 1 or > 64 || RetentionDays is < 1 or > 3650)
            throw new InvalidOperationException("Invalid Monitoring settings: timeout must fit the interval; stale must exceed interval plus timeout.");
    }
}
public sealed record MonitoringStatus(int ModuleId, ModuleHealthResult Health, DateTimeOffset? Timestamp, bool IsStale);
public sealed record MonitoringPoint(DateTimeOffset Timestamp, DateTimeOffset FreshUntil, ModuleHealthState State, string Message);
public sealed record MonitoringBlock(DateTimeOffset Start, DateTimeOffset End, ModuleHealthState State, string Message);
public sealed record HistoryModule(int Id, string Name, bool IsEnabled);
public sealed record HistorySlice(int ModuleId, DateTimeOffset From, DateTimeOffset Until, IReadOnlyList<MonitoringBlock> Blocks, DateTimeOffset? Next);
