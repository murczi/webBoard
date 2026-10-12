namespace Webboard.Domain.Services;
using Model.Modules;

public sealed class ModuleConfigurationRegistry {
    private readonly IReadOnlyDictionary<string, Func<ModuleModel, ModuleConfiguration>> policies =
        new Dictionary<string, Func<ModuleModel, ModuleConfiguration>>(StringComparer.OrdinalIgnoreCase) {
            [ModuleTypes.Http] = m => HttpConfiguration.Create(m.HostId, m.HealthCheckUrl),
            [ModuleTypes.Docker] = m => DockerConfiguration.Create(m.HostId, m.ContainerId),
            [ModuleTypes.Systemd] = m => SystemdConfiguration.Create(m.HostId, m.ServiceName),
            [ModuleTypes.Minecraft] = m => MinecraftConfiguration.Create(m.MinecraftServerAddress, m.MinecraftServerPort),
            [ModuleTypes.Steam] = m => SteamConfiguration.Create(m.SteamServerAddress, m.SteamQueryPort, m.SteamQueryPlayers)
        };
    public ModuleConfigurationRegistry() {
        if (!policies.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(ModuleTypes.All.Select(x => x.Name)))
            throw new InvalidOperationException("Module configuration policies must match the supported type catalog.");
    }
    public ModuleConfiguration Create(string typeName, ModuleModel model) => policies.TryGetValue(typeName, out var factory)
        ? factory(model) : throw new ArgumentException("Unsupported module type.");
}
