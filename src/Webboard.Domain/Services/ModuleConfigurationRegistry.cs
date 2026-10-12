namespace Webboard.Domain.Services;
using Model.Modules;

public sealed class ModuleConfigurationRegistry {
    private readonly IReadOnlyDictionary<string, Func<ModuleModel, ModuleConfiguration>> policies =
        new Dictionary<string, Func<ModuleModel, ModuleConfiguration>>(StringComparer.OrdinalIgnoreCase) {
            ["Http"] = m => HttpConfiguration.Create(m.HostId, m.HealthCheckUrl),
            ["Docker"] = m => DockerConfiguration.Create(m.HostId, m.ContainerId),
            ["Systemd"] = m => SystemdConfiguration.Create(m.HostId, m.ServiceName),
            ["Minecraft"] = m => MinecraftConfiguration.Create(m.MinecraftServerAddress, m.MinecraftServerPort),
            ["Steam"] = m => SteamConfiguration.Create(m.SteamServerAddress, m.SteamQueryPort, m.SteamQueryPlayers)
        };
    public ModuleConfiguration Create(string typeName, ModuleModel model) => policies.TryGetValue(typeName, out var factory)
        ? factory(model) : HttpConfiguration.Create(model.HostId, model.HealthCheckUrl);
}
