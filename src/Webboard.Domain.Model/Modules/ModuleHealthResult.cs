namespace Webboard.Domain.Model.Modules;

public enum ModuleHealthState {
    NotConfigured = 0,
    Healthy = 1,
    Unhealthy = 2,
    Unknown = 3
}

public sealed record ModuleHealthResult(
    ModuleHealthState State,
    long? PingMilliseconds,
    string Message) {
    public SteamServerInfo? Steam { get; init; }
}
