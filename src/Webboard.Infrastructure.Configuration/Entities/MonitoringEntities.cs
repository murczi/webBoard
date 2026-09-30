namespace Webboard.Infrastructure.Configuration.Entities;
using Webboard.Domain.Model.Modules;
public sealed class MonitoringResultEntity {
    public long Id { get; set; }
    public int ModuleId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public DateTimeOffset FreshUntil { get; set; }
    public DateTimeOffset ConfigurationRevision { get; set; }
    public ModuleHealthState State { get; set; }
    public long? PingMilliseconds { get; set; }
    public string Message { get; set; } = "";
}
public sealed class LatestMonitoringEntity {
    public int ModuleId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public DateTimeOffset FreshUntil { get; set; }
    public DateTimeOffset ConfigurationRevision { get; set; }
    public ModuleHealthState State { get; set; }
    public long? PingMilliseconds { get; set; }
    public string Message { get; set; } = "";
    public string? Details { get; set; }
}
