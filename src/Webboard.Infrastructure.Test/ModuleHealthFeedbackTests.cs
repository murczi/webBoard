namespace Webboard.Infrastructure.Test;

using System.Net;
using System.Text;
using Domain.Interfaces.Services;
using Domain.Model.Modules;
using Web.Services;

public sealed class ModuleHealthFeedbackTests {
    [Theory]
    [InlineData("Docker", 404, "Docker container was not found")]
    [InlineData("Systemd", 404, "systemd service was not found")]
    [InlineData("Docker", 503, "Agent is reachable, but Docker is unavailable")]
    [InlineData("Systemd", 503, "Agent is reachable, but systemd is unavailable")]
    [InlineData("Docker", 504, "Agent is reachable, but the Docker request timed out")]
    [InlineData("Systemd", 504, "Agent is reachable, but the systemd request timed out")]
    [InlineData("Docker", 403, "Agent denied access to Docker (HTTP 403)")]
    [InlineData("Docker", 500, "Agent returned HTTP 500 while checking Docker")]
    public async Task AgentErrorsExplainTheFailure(string type, int status, string expected) {
        using var client = Client(_ => new HttpResponseMessage((HttpStatusCode)status));
        var result = await Checker(client).CheckAsync(Module(type));
        Assert.Equal(ModuleHealthState.Unhealthy, result.State);
        Assert.Equal(expected, result.Message);
    }

    [Theory]
    [InlineData("Docker", "null")]
    [InlineData("Docker", "{}")]
    [InlineData("Docker", "{")]
    [InlineData("Docker", "<html>Error</html>")]
    [InlineData("Docker", "{\"isHealthy\":true}")]
    [InlineData("Systemd", "null")]
    [InlineData("Systemd", "{}")]
    [InlineData("Systemd", "{")]
    [InlineData("Systemd", "{\"isHealthy\":false,\"message\":\"\"}")]
    public async Task MalformedAgentPayloadDoesNotBreakTheDashboard(string type, string payload) {
        using var client = Client(_ => Json(payload));
        var result = await Checker(client).CheckAsync(Module(type));
        Assert.Equal(ModuleHealthState.Unhealthy, result.State);
        Assert.Equal("Invalid agent response", result.Message);
    }

    [Theory]
    [InlineData("Docker")]
    [InlineData("Systemd")]
    public async Task UnsupportedResponseContentIsHandled(string type) {
        using var client = Client(_ => new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new UnsupportedContent()
        });
        var result = await Checker(client).CheckAsync(Module(type));
        Assert.Equal("Invalid agent response", result.Message);
    }

    [Theory]
    [InlineData("Docker", false, "Container is stopped")]
    [InlineData("Docker", false, "Container health: unhealthy")]
    [InlineData("Docker", false, "Container health: starting")]
    [InlineData("Docker", true, "Container is running")]
    [InlineData("Systemd", false, "Service is inactive")]
    public async Task AgentReportedStateIsPreserved(string type, bool healthy, string message) {
        using var client = Client(_ => Json(System.Text.Json.JsonSerializer.Serialize(new { isHealthy = healthy, message })));
        var result = await Checker(client).CheckAsync(Module(type));
        Assert.Equal(message, result.Message);
        Assert.Equal(healthy ? ModuleHealthState.Healthy : ModuleHealthState.Unhealthy, result.State);
    }

    [Theory]
    [InlineData("Docker")]
    [InlineData("Systemd")]
    public async Task ConnectionFailuresIdentifyTheHostWithoutClaimingItIsOffline(string type) {
        using var client = Client(_ => throw new HttpRequestException("Connection refused"));
        var result = await Checker(client).CheckAsync(Module(type));
        Assert.Equal("Cannot reach the agent on Lab server", result.Message);
    }

    [Theory]
    [InlineData("Docker")]
    [InlineData("Systemd")]
    public async Task TimeoutsIdentifyTheHost(string type) {
        using var client = Client(_ => throw new TaskCanceledException());
        var result = await Checker(client).CheckAsync(Module(type));
        Assert.Equal("Agent on Lab server did not respond in time", result.Message);
    }

    [Theory]
    [InlineData("Docker")]
    [InlineData("Systemd")]
    [InlineData("Http")]
    public async Task CallerCancellationIsNotReportedAsAnUnhealthyService(string type) {
        using var source = new CancellationTokenSource();
        using var client = Client(_ => { source.Cancel(); throw new OperationCanceledException(source.Token); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Checker(client).CheckAsync(Module(type), source.Token));
    }

    [Theory]
    [InlineData(404, "HTTP 404")]
    [InlineData(503, "HTTP 503")]
    public async Task HttpErrorsPreserveTheirStatusEvenWhenLatencyIsAvailable(int code, string message) {
        using var client = Client(_ => new HttpResponseMessage((HttpStatusCode)code));
        var result = await Checker(client).CheckAsync(Module("Http"));
        Assert.Equal(ModuleHealthState.Unhealthy, result.State);
        Assert.NotNull(result.PingMilliseconds);
        Assert.Equal(message, result.Message);
    }

    [Fact]
    public async Task MissingConfigurationDoesNotMakeRequests() {
        using var client = Client(_ => throw new InvalidOperationException("Must not make requests"));
        var module = Module("Docker");
        module.ContainerId = null;
        Assert.Equal(ModuleHealthState.NotConfigured, (await Checker(client).CheckAsync(module)).State);
        module = Module("Http");
        module.HealthCheckUrl = null;
        Assert.Equal("No health check configured", (await Checker(client).CheckAsync(module)).Message);
    }

    private static ModuleModel Module(string type) => new() {
        Id = 1, Name = "Test", TypeName = type, HostName = "Lab server",
        HostAgentBaseUrl = "http://agent.test", ContainerId = "container",
        ServiceName = "test.service", HealthCheckUrl = "http://service.test/health"
    };

    private static HttpModuleHealthChecker Checker(HttpClient client) =>
        new(client, new DockerAgentClient(client), new SystemdAgentClient(client), new UnusedMinecraftClient());

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> send) => new(new Handler(send));
    private static HttpResponseMessage Json(string payload) => new(HttpStatusCode.OK) {
        Content = new StringContent(payload, Encoding.UTF8, "application/json")
    };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }

    private sealed class UnsupportedContent : HttpContent {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new NotSupportedException("Unsupported content");
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    private sealed class UnusedMinecraftClient : IMinecraftStatusClient {
        public Task<ModuleHealthResult> CheckServerAsync(string address, int port, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Minecraft is not used in these checks.");
    }
}
