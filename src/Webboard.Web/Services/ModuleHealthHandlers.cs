namespace Webboard.Web.Services;
using System.Diagnostics;
using Domain.Interfaces.Services;
using Domain.Model.Modules;

public sealed class HttpHealthHandler(HttpClient httpClient) : IModuleHealthHandler {
    public string TypeName => "Http";
    public async Task<ModuleHealthResult> CheckAsync(ModuleModel module, CancellationToken cancellationToken) {
        var healthCheckUrl = module.HealthCheckUrl;
        if (string.IsNullOrWhiteSpace(healthCheckUrl))
            return new ModuleHealthResult(ModuleHealthState.NotConfigured, null, "No health check configured");
        if (!Uri.TryCreate(healthCheckUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || !string.IsNullOrEmpty(uri.UserInfo))
            return new ModuleHealthResult(ModuleHealthState.Unhealthy, null, "Invalid health-check URL");

        var stopwatch = Stopwatch.StartNew();
        try {
            using var response = await httpClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            stopwatch.Stop();
            var ping = stopwatch.ElapsedMilliseconds;
            var statusCode = (int)response.StatusCode;
            var isReachable = statusCode is >= 200 and < 400;
            return isReachable
                ? new ModuleHealthResult(
                    ModuleHealthState.Healthy,
                    ping,
                    response.IsSuccessStatusCode ? "Healthy" : $"Healthy (HTTP {statusCode} redirect)")
                : new ModuleHealthResult(
                    ModuleHealthState.Unhealthy,
                    ping,
                    $"HTTP {statusCode}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return new ModuleHealthResult(ModuleHealthState.Unhealthy, null, "Health check timed out");
        }
        catch (HttpRequestException) {
            return new ModuleHealthResult(ModuleHealthState.Unhealthy, null, "Cannot reach the health-check endpoint");
        }
    }
}
public sealed class DockerHealthHandler(IDockerAgentClient client) : IModuleHealthHandler {
    public string TypeName => "Docker";
    public async Task<ModuleHealthResult> CheckAsync(ModuleModel module, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(module.HostAgentBaseUrl) || string.IsNullOrWhiteSpace(module.ContainerId)
        ? new(ModuleHealthState.NotConfigured, null, "Docker host or container is not configured")
        : AgentHealthFeedback.WithHost(await client.CheckContainerAsync(module.HostAgentBaseUrl, module.ContainerId, cancellationToken), module.HostName);
}
public sealed class SystemdHealthHandler(ISystemdAgentClient client) : IModuleHealthHandler {
    public string TypeName => "Systemd";
    public async Task<ModuleHealthResult> CheckAsync(ModuleModel module, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(module.HostAgentBaseUrl) || string.IsNullOrWhiteSpace(module.ServiceName)
        ? new(ModuleHealthState.NotConfigured, null, "systemd host or service is not configured")
        : AgentHealthFeedback.WithHost(await client.CheckServiceAsync(module.HostAgentBaseUrl, module.ServiceName, cancellationToken), module.HostName);
}
public sealed class MinecraftHealthHandler(IMinecraftStatusClient client) : IModuleHealthHandler {
    public string TypeName => "Minecraft";
    public Task<ModuleHealthResult> CheckAsync(ModuleModel module, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(module.MinecraftServerAddress) || module.MinecraftServerPort is null
        ? Task.FromResult(new ModuleHealthResult(ModuleHealthState.NotConfigured, null, "Minecraft address or port is not configured"))
        : client.CheckServerAsync(module.MinecraftServerAddress, module.MinecraftServerPort.Value, cancellationToken);
}
public sealed class SteamHealthHandler(ISteamQueryClient client) : IModuleHealthHandler {
    public string TypeName => "Steam";
    public Task<ModuleHealthResult> CheckAsync(ModuleModel module, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(module.SteamServerAddress) || module.SteamQueryPort is null
        ? Task.FromResult(new ModuleHealthResult(ModuleHealthState.NotConfigured, null, "Steam address or query port is not configured"))
        : client.CheckAsync(module.SteamServerAddress, module.SteamQueryPort.Value, module.SteamQueryPlayers, cancellationToken);
}
