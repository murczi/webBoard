namespace Webboard.Domain.Model.Modules;

public enum ModuleHealthState {
    NotConfigured,
    Healthy,
    Unhealthy
}

public sealed record ModuleHealthResult(
    ModuleHealthState State,
    long? PingMilliseconds,
    string Message);
