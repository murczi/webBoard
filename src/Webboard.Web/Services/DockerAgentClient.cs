namespace Webboard.Web.Services;

using System.Diagnostics;
using System.Net.Http.Json;
using Domain.Interfaces.Services;
using Domain.Model.Modules;

public sealed class DockerAgentClient(HttpClient httpClient) : IDockerAgentClient {
    public async Task<IReadOnlyList<DockerContainerModel>> GetContainersAsync(
        string agentBaseUrl,
        CancellationToken cancellationToken = default) {
        var endpoint = CreateEndpoint(agentBaseUrl, "docker/containers");
        using var response = await httpClient.GetAsync(endpoint, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<DockerContainerModel>>(
            cancellationToken: cancellationToken) ?? [];
    }

    public async Task<ModuleHealthResult> CheckContainerAsync(
        string agentBaseUrl,
        string containerId,
        CancellationToken cancellationToken = default) {
        Uri endpoint;
        try {
            endpoint = CreateEndpoint(
                agentBaseUrl,
                $"docker/containers/{Uri.EscapeDataString(containerId)}/status");
        }
        catch (ArgumentException) {
            return new ModuleHealthResult(ModuleHealthState.Unhealthy, null, "Invalid agent URL");
        }

        var stopwatch = Stopwatch.StartNew();
        try {
            using var response = await httpClient.GetAsync(endpoint, cancellationToken);
            stopwatch.Stop();
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new ModuleHealthResult(
                    ModuleHealthState.Unhealthy,
                    stopwatch.ElapsedMilliseconds,
                    "Docker container was not found");
            if (!response.IsSuccessStatusCode)
                return new ModuleHealthResult(
                    ModuleHealthState.Unhealthy,
                    stopwatch.ElapsedMilliseconds,
                    $"Agent returned HTTP {(int)response.StatusCode}");

            var status = await response.Content.ReadFromJsonAsync<DockerContainerStatus>(
                cancellationToken: cancellationToken);
            if (status is null)
                return new ModuleHealthResult(ModuleHealthState.Unhealthy, null, "Invalid agent response");
            return new ModuleHealthResult(
                status.IsHealthy ? ModuleHealthState.Healthy : ModuleHealthState.Unhealthy,
                stopwatch.ElapsedMilliseconds,
                status.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return new ModuleHealthResult(ModuleHealthState.Unhealthy, null, "Docker check timed out");
        }
        catch (HttpRequestException) {
            return new ModuleHealthResult(ModuleHealthState.Unhealthy, null, "Docker agent could not be reached");
        }
    }

    private static Uri CreateEndpoint(string agentBaseUrl, string relativePath) {
        if (!Uri.TryCreate(agentBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Invalid agent URL.", nameof(agentBaseUrl));
        return new Uri(baseUri, relativePath);
    }

    private sealed record DockerContainerStatus(bool IsHealthy, string Message);
}
