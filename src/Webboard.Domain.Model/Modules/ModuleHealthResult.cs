namespace Webboard.Domain.Model.Modules;

public enum ModuleHealthState {
    NotConfigured,
    Healthy,
    Unhealthy,
    Unknown
}

public sealed record ModuleHealthResult(
    ModuleHealthState State,
    long? PingMilliseconds,
    string Message) {
    public SteamServerInfo? Steam { get; init; }
}
