namespace Webboard.Web.Services;

using System.Text.Json;

public enum AgentResponseFailure { Malformed, Unreadable }
public sealed class AgentResponseException(AgentResponseFailure failure, Exception? inner = null)
    : HttpRequestException("Agent returned an invalid or oversized response.", inner) {
    public AgentResponseFailure Failure { get; } = failure;
}

public static class AgentResponseReader {
    public static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken token, int maximumBytes = 1048576) {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try {
            await response.Content.LoadIntoBufferAsync(maximumBytes, deadline.Token);
            return await response.Content.ReadFromJsonAsync<T>(deadline.Token)
                ?? throw new AgentResponseException(AgentResponseFailure.Malformed);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException) {
            throw new AgentResponseException(AgentResponseFailure.Malformed, exception);
        }
        catch (HttpRequestException exception) when (exception is not AgentResponseException) {
            throw new AgentResponseException(AgentResponseFailure.Unreadable, exception);
        }
    }
}
