namespace Webboard.Web.Services;

using System.Net.Http.Json;
using Webboard.Contracts;
using Domain.Interfaces.Services;
using Domain.Model.Hosts;

public sealed class AgentHealthChecker(HttpClient httpClient) : IAgentHealthChecker {
    public async Task<AgentHealthResult> CheckAsync(
        string agentBaseUrl,
        CancellationToken cancellationToken = default) {
        if (!TryBuildHealthUri(agentBaseUrl, out var healthUri))
            return new AgentHealthResult(false, "Enter a valid absolute HTTP or HTTPS agent base URL.");

        try {
            using var response = await httpClient.GetAsync(healthUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new AgentHealthResult(false, $"Agent health check returned HTTP {(int)response.StatusCode}.");

            var payload = await AgentResponseReader.ReadAsync<AgentHealthResponse>(response, cancellationToken, 16384);
            if (!AgentProtocol.Supports(payload.ProtocolVersion)) return new AgentHealthResult(false, "Agent protocol version is not supported.");
            return string.Equals(payload.Status, "Healthy", StringComparison.OrdinalIgnoreCase)
                ? new AgentHealthResult(true, "Agent is healthy.")
                : new AgentHealthResult(false, "Agent did not report a healthy status.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return new AgentHealthResult(false, "Agent health check timed out.");
        }
        catch (AgentResponseException) {
            return new AgentHealthResult(false, "Agent returned an invalid health response.");
        }
        catch (HttpRequestException) {
            return new AgentHealthResult(false, "Agent could not be reached.");
        }
        catch (System.Text.Json.JsonException) {
            return new AgentHealthResult(false, "Agent returned an invalid health response.");
        }
        catch (NotSupportedException) {
            return new AgentHealthResult(false, "Agent returned an invalid health response.");
        }
    }

    private static bool TryBuildHealthUri(string value, out Uri? healthUri) {
        try { healthUri = AgentEndpoint.Create(value).For("health"); return true; }
        catch (ArgumentException) { healthUri = null; return false; }
    }

}
