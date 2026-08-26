namespace Webboard.Web.Services;

using System.Diagnostics;
using Domain.Interfaces.Services;
using Domain.Model.Modules;

public sealed class HttpModuleHealthChecker(HttpClient httpClient) : IModuleHealthChecker {
    public async Task<ModuleHealthResult> CheckAsync(
        string? healthCheckUrl,
        CancellationToken cancellationToken = default) {
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
