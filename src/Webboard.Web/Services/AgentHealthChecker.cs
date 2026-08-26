namespace Webboard.Web.Services;

using System.Net.Http.Json;
using Domain.Interfaces.Services;
using Domain.Model.Hosts;

public sealed class AgentHealthChecker(HttpClient httpClient) : IAgentHealthChecker {
    public async Task<AgentHealthResult> CheckAsync(
        string agentBaseUrl,
        CancellationToken cancellationToken = default) {
        if (!TryBuildHealthUri(agentBaseUrl, out var healthUri))
            return new AgentHealthResult(false, "Enter a valid absolute HTTP or HTTPS agent base URL.");

        try {
            using var response = await httpClient.GetAsync(healthUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new AgentHealthResult(false, $"Agent health check returned HTTP {(int)response.StatusCode}.");

            var payload = await response.Content.ReadFromJsonAsync<HealthResponse>(cancellationToken);
            return string.Equals(payload?.Status, "Healthy", StringComparison.OrdinalIgnoreCase)
                ? new AgentHealthResult(true, "Agent is healthy.")
                : new AgentHealthResult(false, "Agent did not report a healthy status.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return new AgentHealthResult(false, "Agent health check timed out.");
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
        healthUri = null;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(baseUri.UserInfo) ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment))
            return false;

        var builder = new UriBuilder(baseUri) { Path = $"{baseUri.AbsolutePath.TrimEnd('/')}/health" };
        healthUri = builder.Uri;
        return true;
    }

    private sealed class HealthResponse {
        public string? Status { get; init; }
    }
}
