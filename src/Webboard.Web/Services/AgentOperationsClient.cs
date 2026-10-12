namespace Webboard.Web.Services;
using Domain.Interfaces.Services;
using Domain.Model.Modules;
public sealed class AgentOperationsClient(HttpClient client) : IAgentOperationsClient {
    public async Task<ModuleOperationResult> ExecuteAsync(string baseUrl, Guid requestId, int moduleId, string kind, string target, string operation, CancellationToken cancellationToken) {
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/operations") {
            Content = JsonContent.Create(new { requestId, moduleId, kind, target, operation })
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        ModuleOperationResult? result = null;
        try {
            await response.Content.LoadIntoBufferAsync(131072, cancellationToken);
            result = await response.Content.ReadFromJsonAsync<ModuleOperationResult>(cancellationToken);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or NotSupportedException or HttpRequestException) { }
        var rejected = response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized
            or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Conflict;
        if (result is not null && !string.IsNullOrWhiteSpace(result.Message) && result.Message.Length <= 1000 && result.Output?.Length is not > 16384
            && Enum.IsDefined(result.Outcome) && Enum.IsDefined(result.FailureReason)
            && result.Success == (result.Outcome == OperationOutcome.Succeeded) && (response.IsSuccessStatusCode || !result.Success))
            return result;
        return new(false, rejected ? "Agent rejected the operation." : "Operation outcome is unknown. Verify target state before retrying.",
            TimedOut: response.StatusCode == System.Net.HttpStatusCode.GatewayTimeout) {
            Outcome = rejected ? OperationOutcome.Rejected : OperationOutcome.Unknown,
            FailureReason = response.StatusCode switch {
                System.Net.HttpStatusCode.GatewayTimeout => OperationFailureReason.Timeout,
                System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => OperationFailureReason.Authorization,
                System.Net.HttpStatusCode.Conflict => OperationFailureReason.Busy,
                _ => response.IsSuccessStatusCode ? OperationFailureReason.InvalidResponse : OperationFailureReason.Transport
            }
        };
    }

    public async Task<IReadOnlyList<string>> GetAllowedOperationsAsync(string baseUrl, string kind, string target, CancellationToken token) {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        return await client.GetFromJsonAsync<List<string>>($"{baseUrl.TrimEnd('/')}/capabilities?kind={Uri.EscapeDataString(kind)}&target={Uri.EscapeDataString(target)}", timeout.Token) ?? [];
    }
}
