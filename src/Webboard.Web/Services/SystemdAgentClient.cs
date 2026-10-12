namespace Webboard.Web.Services;

using System.Net.Http.Json;
using System.Text.Json;
using Webboard.Contracts;
using Domain.Interfaces.Services;
using Domain.Model.Modules;
using Domain.Model.Hosts;

public sealed class SystemdAgentClient(HttpClient httpClient) : ISystemdAgentClient {
    public async Task<IReadOnlyList<SystemdServiceModel>> GetServicesAsync(
        string agentBaseUrl,
        CancellationToken cancellationToken = default) {
        using var response = await httpClient.GetAsync(
            CreateEndpoint(agentBaseUrl, "systemd/services"), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) {
            AgentProblem? problem = null;
            try {
                problem = await AgentResponseReader.ReadAsync<AgentProblem>(response, cancellationToken, 16384);
            }
            catch (AgentResponseException) {
                // Preserve the HTTP failure when an intermediary returns non-JSON content.
            }
            throw new HttpRequestException(
                problem?.Detail ?? $"Agent returned HTTP {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        }
        var services = await AgentResponseReader.ReadAsync<List<SystemdServiceDto>>(response, cancellationToken);
        return services.Select(x => new SystemdServiceModel(x.Name, x.Description, x.LoadState, x.ActiveState, x.SubState)).ToArray();
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
            using var response = await httpClient.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            receivedResponse = true;
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new ModuleHealthResult(
                    ModuleHealthState.Unhealthy, null, "systemd service was not found");
            if (!response.IsSuccessStatusCode)
                return new ModuleHealthResult(
                    ModuleHealthState.Unhealthy, null,
                    AgentHealthFeedback.ResponseFailure(response.StatusCode, "systemd"));

            var status = await AgentResponseReader.ReadAsync<SystemdServiceStatusDto>(response, cancellationToken);
            return status is null || string.IsNullOrWhiteSpace(status.Message)
                ? new ModuleHealthResult(ModuleHealthState.Unhealthy, null, "Invalid agent response")
                : new ModuleHealthResult(
                    status.IsHealthy == true ? ModuleHealthState.Healthy : ModuleHealthState.Unhealthy,
                    null,
                    status.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return new ModuleHealthResult(ModuleHealthState.Unhealthy, null, "Host agent request timed out");
        }
        catch (AgentResponseException) {
            return new ModuleHealthResult(ModuleHealthState.Unhealthy, null, "Invalid agent response");
        }
        catch (HttpRequestException) {
            return new ModuleHealthResult(ModuleHealthState.Unhealthy, null, receivedResponse ? "Could not read the agent response" : "Cannot reach the host agent");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException) {
            return new ModuleHealthResult(ModuleHealthState.Unhealthy, null, "Invalid agent response");
        }
    }

    private static Uri CreateEndpoint(string agentBaseUrl, string relativePath) => AgentEndpoint.Create(agentBaseUrl).For(relativePath);


    private sealed record AgentProblem(string? Detail);
}
