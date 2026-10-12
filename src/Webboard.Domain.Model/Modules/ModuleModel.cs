namespace Webboard.Domain.Model.Modules;

public class ModuleModel {
    public ModuleConfiguration? Configuration { get; private set; }
    public void ApplyConfiguration(ModuleConfiguration configuration) {
        Configuration = configuration; TypeName = configuration.TypeName; HostId = configuration.HostId;
        HealthCheckUrl = (configuration as HttpConfiguration)?.HealthCheckUrl;
        ContainerId = (configuration as DockerConfiguration)?.Container;
        ServiceName = (configuration as SystemdConfiguration)?.ServiceName;
        MinecraftServerAddress = (configuration as MinecraftConfiguration)?.Endpoint.Address;
        MinecraftServerPort = (configuration as MinecraftConfiguration)?.Endpoint.Port;
        SteamServerAddress = (configuration as SteamConfiguration)?.Endpoint.Address;
        SteamQueryPort = (configuration as SteamConfiguration)?.Endpoint.Port;
        SteamQueryPlayers = (configuration as SteamConfiguration)?.QueryPlayers ?? false;
    }
    public int Id { get; set; }
    public int TypeId { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public int? HostId { get; set; }
    public bool HostExists { get; set; }
    public bool HostIsEnabled { get; set; }
    public bool HostIsDeleted { get; set; }
    public string? HostName { get; set; }
    public string? HostAgentBaseUrl { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? HealthCheckUrl { get; set; }
    // Docker name for stable tracking; legacy modules may still contain an ID.
    public string? ContainerId { get; set; }
    public string? ServiceName { get; set; }
    public string? MinecraftServerAddress { get; set; }
    public int? MinecraftServerPort { get; set; }
    public string? SteamServerAddress { get; set; }
    public int? SteamQueryPort { get; set; }
    public bool SteamQueryPlayers { get; set; }
    public string? ManagementUrl { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset DateCreated { get; set; }
    public DateTimeOffset DateUpdated { get; set; }
}
