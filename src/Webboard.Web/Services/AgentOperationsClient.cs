namespace Webboard.Web.Services;
using Domain.Interfaces.Services;
using Domain.Model.Modules;
public sealed class AgentOperationsClient(HttpClient client) : IAgentOperationsClient {
    public async Task<ModuleOperationResult> ExecuteAsync(string baseUrl, Guid requestId, int moduleId, string kind, string target, string operation, CancellationToken cancellationToken) {
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/operations") {
            Content = JsonContent.Create(new { requestId, moduleId, kind, target, operation })
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) return new(false, response.StatusCode switch {
            System.Net.HttpStatusCode.Forbidden => "Agent does not allow this operation on the target.",
            System.Net.HttpStatusCode.Unauthorized => "Agent authentication failed.",
            System.Net.HttpStatusCode.Conflict => "Another operation is running on this agent.",
            _ => $"Agent returned HTTP {(int)response.StatusCode}; verify target state before retrying."
        });
        await response.Content.LoadIntoBufferAsync(131072, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<ModuleOperationResult>(cancellationToken);
        return result is null || string.IsNullOrWhiteSpace(result.Message) || result.Message.Length > 1000 || result.Output?.Length > 16384
            ? new(false, "Invalid agent response; operation outcome is unknown. Verify target state.") : result;
    }
    public async Task<IReadOnlyList<string>> GetAllowedOperationsAsync(string baseUrl, string kind, string target, CancellationToken token) {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        return await client.GetFromJsonAsync<List<string>>($"{baseUrl.TrimEnd('/')}/capabilities?kind={Uri.EscapeDataString(kind)}&target={Uri.EscapeDataString(target)}", timeout.Token) ?? [];
    }
}
