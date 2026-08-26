namespace Webboard.Web.Services;

using System.Diagnostics;
using Domain.Interfaces.Services;
using Domain.Model.Modules;

public sealed class HttpModuleHealthChecker(
    HttpClient httpClient,
    IDockerAgentClient dockerAgent,
    ISystemdAgentClient systemdAgent,
    IMinecraftStatusClient minecraftStatus) : IModuleHealthChecker {
    public async Task<ModuleHealthResult> CheckAsync(
        ModuleModel module,
        CancellationToken cancellationToken = default) {
        if (string.Equals(module.TypeName, "Docker", StringComparison.OrdinalIgnoreCase)) {
            if (string.IsNullOrWhiteSpace(module.HostAgentBaseUrl) ||
                string.IsNullOrWhiteSpace(module.ContainerId))
                return new ModuleHealthResult(
                    ModuleHealthState.NotConfigured,
                    null,
                    "Docker host or container is not configured");
            return await dockerAgent.CheckContainerAsync(
                module.HostAgentBaseUrl,
                module.ContainerId,
                cancellationToken);
        }

        if (string.Equals(module.TypeName, "Systemd", StringComparison.OrdinalIgnoreCase)) {
            if (string.IsNullOrWhiteSpace(module.HostAgentBaseUrl) ||
                string.IsNullOrWhiteSpace(module.ServiceName))
                return new ModuleHealthResult(
                    ModuleHealthState.NotConfigured,
                    null,
                    "systemd host or service is not configured");
            return await systemdAgent.CheckServiceAsync(
                module.HostAgentBaseUrl,
                module.ServiceName,
                cancellationToken);
        }

        if (string.Equals(module.TypeName, "Minecraft", StringComparison.OrdinalIgnoreCase)) {
            if (string.IsNullOrWhiteSpace(module.MinecraftServerAddress) ||
                module.MinecraftServerPort is null)
                return new ModuleHealthResult(
                    ModuleHealthState.NotConfigured,
                    null,
                    "Minecraft address or port is not configured");
            return await minecraftStatus.CheckServerAsync(
                module.MinecraftServerAddress,
                module.MinecraftServerPort.Value,
                cancellationToken);
        }

        var healthCheckUrl = module.HealthCheckUrl;
        if (string.IsNullOrWhiteSpace(healthCheckUrl))
            return new ModuleHealthResult(ModuleHealthState.NotConfigured, null, "No health check configured");
        if (!Uri.TryCreate(healthCheckUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
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
            return new ModuleHealthResult(ModuleHealthState.Unhealthy, null, "Health check failed");
        }
    }
}
