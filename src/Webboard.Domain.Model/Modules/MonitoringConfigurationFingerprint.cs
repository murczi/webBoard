namespace Webboard.Domain.Model.Modules;

// Presentation metadata deliberately does not participate in health freshness.
public sealed record MonitoringConfigurationFingerprint(int TypeId, int? HostId, bool IsEnabled,
    string? HealthCheckUrl, string? ContainerId, string? ServiceName, string? MinecraftAddress,
    int? MinecraftPort, string? SteamAddress, int? SteamPort, bool QueryPlayers) {
    public static MonitoringConfigurationFingerprint From(ModuleModel module) => new(module.TypeId,
        module.HostId, module.IsEnabled, module.HealthCheckUrl, module.ContainerId, module.ServiceName,
        module.MinecraftServerAddress, module.MinecraftServerPort, module.SteamServerAddress,
        module.SteamQueryPort, module.SteamQueryPlayers);
}
