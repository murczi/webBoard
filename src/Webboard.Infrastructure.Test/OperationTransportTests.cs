namespace Webboard.Infrastructure.Test;

using System.Net;
using System.Text;
using Domain.Model.Modules;
using Web.Services;

public sealed class OperationTransportTests {
    [Theory]
    [InlineData(504, "{\"success\":false,\"message\":\"Timed out\",\"timedOut\":true,\"outcome\":\"Unknown\",\"failureReason\":\"Timeout\"}", true, OperationOutcome.Unknown)]
    [InlineData(502, "<html>Gateway failure</html>", false, OperationOutcome.Unknown)]
    [InlineData(403, "", false, OperationOutcome.Rejected)]
    [InlineData(200, "{\"success\":true,\"message\":\"Done\"}", false, OperationOutcome.Succeeded)]
    public async Task ErrorResponsesPreserveTypedOutcomeAndLegacySuccess(int status, string body, bool timeout, OperationOutcome outcome) {
        using var client = new HttpClient(new Handler(status, body));
        var result = await new AgentOperationsClient(client).ExecuteAsync("https://agent.test", Guid.NewGuid(), 1, "Docker", "target", "restart", default);
        Assert.Equal(timeout, result.TimedOut); Assert.Equal(outcome, result.Outcome);
    }
    private sealed class Handler(int status, string body) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
