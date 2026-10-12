namespace Webboard.Domain.Model.Modules;

public static class WebUrl {
    public static string? NormalizeOptional(string? value, string label) {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException($"{label} must be an absolute HTTP or HTTPS URL without credentials.");
        if (uri.AbsoluteUri.Length > 2048) throw new ArgumentException($"{label} cannot exceed 2048 characters.");
        return uri.AbsoluteUri;
    }
}

public abstract class ModuleConfiguration {
    private protected ModuleConfiguration() { }
    public abstract string TypeName { get; }
    public virtual int? HostId => null;
}
public sealed class HttpConfiguration : ModuleConfiguration {
    private HttpConfiguration(int? hostId, string? url) { HostId = hostId; HealthCheckUrl = url; }
    public override string TypeName => "Http";
    public override int? HostId { get; }
    public string? HealthCheckUrl { get; }
    public static HttpConfiguration Create(int? hostId, string? url) => new(hostId, WebUrl.NormalizeOptional(url, "Health-check URL"));
}
public sealed class DockerConfiguration : ModuleConfiguration {
    private DockerConfiguration(int hostId, string container) { HostId = hostId; Container = container; }
    public override string TypeName => "Docker";
    public override int? HostId { get; }
    public string Container { get; }
    public static DockerConfiguration Create(int? hostId, string? container) {
        if (hostId is null or <= 0) throw new ArgumentException("Select a host for a Docker module.");
        if (string.IsNullOrWhiteSpace(container) || container.Trim().Length > 128) throw new ArgumentException("Select a Docker container of at most 128 characters.");
        return new(hostId.Value, container.Trim());
    }
}
public sealed class SystemdConfiguration : ModuleConfiguration {
    private SystemdConfiguration(int hostId, string service) { HostId = hostId; ServiceName = service; }
    public override string TypeName => "Systemd";
    public override int? HostId { get; }
    public string ServiceName { get; }
    public static SystemdConfiguration Create(int? hostId, string? service) {
        if (hostId is null or <= 0) throw new ArgumentException("Select a host for a systemd module.");
        if (!SystemdUnitName.IsValid(service?.Trim())) throw new ArgumentException("Select a valid systemd service.");
        return new(hostId.Value, service!.Trim());
    }
}
public sealed class ServerEndpoint {
    private ServerEndpoint(string address, int port) { Address = address; Port = port; }
    public string Address { get; }
    public int Port { get; }
    public static ServerEndpoint Create(string? address, int? port, string label) {
        if (!ServerAddress.IsValid(address?.Trim())) throw new ArgumentException($"Enter a valid {label} server address.");
        if (port is null or < 1 or > 65535) throw new ArgumentException($"{label} server port must be between 1 and 65535.");
        return new(address!.Trim(), port.Value);
    }
}
public sealed class MinecraftConfiguration : ModuleConfiguration {
    private MinecraftConfiguration(ServerEndpoint endpoint) => Endpoint = endpoint;
    public override string TypeName => "Minecraft";
    public ServerEndpoint Endpoint { get; }
    public static MinecraftConfiguration Create(string? address, int? port) => new(ServerEndpoint.Create(address, port, "Minecraft"));
}
public sealed class SteamConfiguration : ModuleConfiguration {
    private SteamConfiguration(ServerEndpoint endpoint, bool players) { Endpoint = endpoint; QueryPlayers = players; }
    public override string TypeName => "Steam";
    public ServerEndpoint Endpoint { get; }
    public bool QueryPlayers { get; }
    public static SteamConfiguration Create(string? address, int? port, bool players) => new(ServerEndpoint.Create(address, port, "Steam"), players);
}
