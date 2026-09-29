namespace Webboard.Web.Services;

using System.Net.Http.Json;
using System.Text.Json;
using Domain.Interfaces.Services;
using Domain.Model.Modules;

public sealed class SystemdAgentClient(HttpClient httpClient) : ISystemdAgentClient {
    public async Task<IReadOnlyList<SystemdServiceModel>> GetServicesAsync(
        string agentBaseUrl,
        CancellationToken cancellationToken = default) {
        using var response = await httpClient.GetAsync(
            CreateEndpoint(agentBaseUrl, "systemd/services"), cancellationToken);
        if (!response.IsSuccessStatusCode) {
            AgentProblem? problem = null;
            try {
                problem = await response.Content.ReadFromJsonAsync<AgentProblem>(
                    cancellationToken: cancellationToken);
            }
            catch (JsonException) {
                // Preserve the HTTP failure when an intermediary returns non-JSON content.
            }
            throw new HttpRequestException(
                problem?.Detail ?? $"Agent returned HTTP {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<List<SystemdServiceModel>>(
            cancellationToken: cancellationToken) ?? [];
    }

    public async Task<ModuleHealthResult> CheckServiceAsync(
        string agentBaseUrl,
        string serviceName,
        CancellationToken cancellationToken = default) {
        Uri endpoint;
        try {
            endpoint = CreateEndpoint(
                agentBaseUrl,
                $"systemd/services/{Uri.EscapeDataString(serviceName)}/status");
        }
        catch (ArgumentException) {
            return new ModuleHealthResult(ModuleHealthState.Unhealthy, null, "Invalid agent URL");
        }

        var receivedResponse = false;
        try {
            using var response = await httpClient.GetAsync(endpoint, cancellationToken);
            receivedResponse = true;
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new ModuleHealthResult(
                    ModuleHealthState.Unhealthy, null, "systemd service was not found");
            if (!response.IsSuccessStatusCode)
                return new ModuleHealthResult(
                    ModuleHealthState.Unhealthy, null,
                    AgentHealthFeedback.ResponseFailure(response.StatusCode, "systemd"));

            var status = await response.Content.ReadFromJsonAsync<SystemdServiceStatus>(
                cancellationToken: cancellationToken);
            return status is null || status.IsHealthy is null || string.IsNullOrWhiteSpace(status.Message)
                ? new ModuleHealthResult(ModuleHealthState.Unhealthy, null, "Invalid agent response")
                : new ModuleHealthResult(
                    status.IsHealthy == true ? ModuleHealthState.Healthy : ModuleHealthState.Unhealthy,
                    null,
                    status.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return new ModuleHealthResult(ModuleHealthState.Unhealthy, null, "Host agent request timed out");
        }
        catch (HttpRequestException) {
            return new ModuleHealthResult(ModuleHealthState.Unhealthy, null, receivedResponse ? "Could not read the agent response" : "Cannot reach the host agent");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException) {
            return new ModuleHealthResult(ModuleHealthState.Unhealthy, null, "Invalid agent response");
        }
    }

    private static Uri CreateEndpoint(string agentBaseUrl, string relativePath) {
        if (!Uri.TryCreate(agentBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Invalid agent URL.", nameof(agentBaseUrl));
        return new Uri(baseUri, relativePath);
    }

    private sealed record SystemdServiceStatus(bool? IsHealthy, string? Message);
    private sealed record AgentProblem(string? Detail);
}
