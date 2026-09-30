namespace Webboard.Infrastructure.Test;
using System.Net;
using System.Net.Sockets;
using System.Text;
using WebBoard.Agent;

public sealed class AgentOperationTests {
    [Theory]
    [InlineData("start", "example")] [InlineData("stop", "example")] [InlineData("restart", "example")]
    [InlineData("start", "012345")] [InlineData("stop", "012345")] [InlineData("restart", "012345")]
    public async Task AllowedDockerTargetPermitsAllOperationsUsingTheConfiguredSocket(string operation, string allowedTarget) {
        var directory = Path.Combine(Path.GetTempPath(), "webboard-socket-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "docker.sock");
        try {
            using var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            server.Bind(new UnixDomainSocketEndPoint(path)); server.Listen(1);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var serve = Task.Run(async () => {
                using (var inspectSocket = await server.AcceptAsync(deadline.Token)) {
                    using var inspectStream = new NetworkStream(inspectSocket);
                    using var inspectReader = new StreamReader(inspectStream, Encoding.ASCII, leaveOpen: true);
                    Assert.Equal("GET /containers/example/json HTTP/1.1", await inspectReader.ReadLineAsync(deadline.Token));
                    while (!string.IsNullOrEmpty(await inspectReader.ReadLineAsync(deadline.Token))) { }
                    const string body = "{\"Id\":\"012345\",\"Name\":\"/example\",\"State\":{\"Status\":\"running\",\"Running\":true}}";
                    await inspectStream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}"), deadline.Token);
                }
                using var socket = await server.AcceptAsync(deadline.Token);
                using var stream = new NetworkStream(socket);
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var line = await reader.ReadLineAsync(deadline.Token);
                Assert.Equal($"POST /containers/012345/{operation}?t=10 HTTP/1.1", line);
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(deadline.Token))) { }
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), deadline.Token);
            });
            var config = AgentSecurityTests.Config(new() { ["Docker:SocketPath"] = path, ["Controls:Docker:0"] = allowedTarget });
            using var client = new DockerSocketClient(config);
            var runner = new CapturingRunner();
            Assert.True((await new AgentOperations(config, client, runner)
                .ExecuteAsync(new(Guid.NewGuid(), 1, "Docker", "example", operation), deadline.Token)).Success);
            Assert.Empty(runner.Calls);
            await serve;
        }
        finally { Directory.Delete(directory, true); }
    }
    [Theory]
    [InlineData("start")] [InlineData("stop")] [InlineData("restart")]
    public async Task SystemdRuntimeControlsUseFixedArguments(string operation) {
        var config = AgentSecurityTests.Config(new() { ["Controls:Systemd:0"] = "example.service" });
        using var docker = new DockerSocketClient(config); var runner = new CapturingRunner();
        var operations = new AgentOperations(config, docker, runner);
        Assert.True((await operations.ExecuteAsync(new(Guid.NewGuid(), 1, "Systemd", "example.service", operation), default)).Success);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("/usr/bin/systemctl", call.Executable);
        Assert.Equal(new[] { "--no-ask-password", operation, "--", "example.service" }, call.Arguments);
        await Assert.ThrowsAsync<ArgumentException>(() => operations.ExecuteAsync(new(Guid.NewGuid(), 1, "Systemd", "--danger.service", operation), default));
    }
    [Theory]
    [InlineData("enable", "EnableUnitFiles")] [InlineData("disable", "DisableUnitFiles")]
    public async Task UnitFileControlsUseHostBusAndReload(string operation, string method) {
        var runner = new CapturingRunner();
        var result = await new SystemdClient(runner).ControlAsync("example.service", operation, default);
        Assert.True(result.Success); Assert.Null(result.Output); Assert.Equal(2, runner.Calls.Count);
        var expected = new[] { "--system", "--allow-interactive-authorization=no", "--timeout=30s", "call",
            "org.freedesktop.systemd1", "/org/freedesktop/systemd1", "org.freedesktop.systemd1.Manager", method }
            .Concat(operation == "enable" ? new[] { "asbb", "1", "example.service", "false", "false" } : new[] { "asb", "1", "example.service", "false" });
        Assert.Equal("/usr/bin/busctl", runner.Calls[0].Executable);
        Assert.Equal(expected, runner.Calls[0].Arguments);
        Assert.Equal("/usr/bin/busctl", runner.Calls[1].Executable);
        Assert.Equal("Reload", runner.Calls[1].Arguments.Last());
        Assert.Equal(8, runner.Calls[1].Arguments.Count);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailedUnitFileChangeDoesNotReloadAndReloadFailureReportsPartialChange(bool failReload) {
        var runner = new CapturingRunner();
        runner.Results.Enqueue(new(failReload, failReload ? "Done" : "Permission denied", 1, "private output"));
        runner.Results.Enqueue(new(false, "Permission denied", 1, "private output"));
        var result = await new SystemdClient(runner).ControlAsync("example.service", "enable", default);
        Assert.False(result.Success); Assert.Null(result.Output);
        Assert.Equal(failReload ? 2 : 1, runner.Calls.Count);
        if (failReload) Assert.Contains("Unit-file change completed", result.Message);
    }
    [Fact]
    public async Task ConcurrentOperationsAreRejectedAndFailedExitCodesArePreserved() {
        var config = AgentSecurityTests.Config(new() { ["Controls:Systemd:0"] = "example.service" });
        using var docker = new DockerSocketClient(config);
        var release = new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new CapturingRunner { Result = release.Task };
        var operations = new AgentOperations(config, docker, runner);
        var request = new OperationRequest(Guid.NewGuid(), 1, "Systemd", "example.service", "restart");
        var first = operations.ExecuteAsync(request, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => operations.ExecuteAsync(request with { RequestId = Guid.NewGuid() }, default));
        release.SetResult(new(false, "Failed", 19)); Assert.Equal(19, (await first).ExitCode);
    }
    [Theory]
    [InlineData("Command", "example.service", "execute")]
    [InlineData("Docker", "example", "enable")]
    [InlineData("Systemd", "example.service", "execute")]
    [InlineData("Systemd", "../example.service", "start")]
    public async Task UnsupportedOperationsNeverRun(string kind, string target, string operation) {
        var config = AgentSecurityTests.Config([]);
        using var docker = new DockerSocketClient(config); var runner = new CapturingRunner();
        await Assert.ThrowsAsync<ArgumentException>(() => new AgentOperations(config, docker, runner)
            .ExecuteAsync(new(Guid.NewGuid(), 1, kind, target, operation), default));
        Assert.Empty(runner.Calls);
    }
    [Theory]
    [InlineData("start")] [InlineData("stop")] [InlineData("restart")] [InlineData("enable")] [InlineData("disable")]
    public async Task EverySystemdOperationRequiresAnAllowedTarget(string operation) {
        var config = AgentSecurityTests.Config([]);
        using var docker = new DockerSocketClient(config); var runner = new CapturingRunner();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new AgentOperations(config, docker, runner)
            .ExecuteAsync(new(Guid.NewGuid(), 1, "Systemd", "example.service", operation), default));
        Assert.Empty(runner.Calls);
    }
    [Theory]
    [InlineData("start")] [InlineData("stop")] [InlineData("restart")] [InlineData("enable")] [InlineData("disable")]
    public async Task AllowedSystemdTargetPermitsEverySupportedOperation(string operation) {
        var config = AgentSecurityTests.Config(new() { ["Controls:Systemd:0"] = "example.service" });
        using var docker = new DockerSocketClient(config); var runner = new CapturingRunner();
        var operations = new AgentOperations(config, docker, runner);
        Assert.True((await operations.ExecuteAsync(new(Guid.NewGuid(), 1, "Systemd", "example.service", operation), default)).Success);
        Assert.NotEmpty(runner.Calls);
        var calls = runner.Calls.Count;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operations
            .ExecuteAsync(new(Guid.NewGuid(), 1, "Systemd", "other.service", operation), default));
        Assert.Equal(calls, runner.Calls.Count);
    }
    [Fact]
    public async Task FailedProcessReturnsExitCode() {
        var result = await new ProcessRunner().RunAsync("/usr/bin/false", [], TimeSpan.FromSeconds(1), default);
        Assert.False(result.Success); Assert.Equal(1, result.ExitCode);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CapabilityDiscoveryHonorsDeadlineAndCallerCancellation(bool cancelCaller) {
        using var handler = new WaitingHandler();
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(310) };
        using var caller = new CancellationTokenSource();
        var discovery = new Web.Services.AgentOperationsClient(client).GetAllowedOperationsAsync("https://agent.test", "Systemd", "example.service", caller.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (cancelCaller) caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await discovery.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(handler.Cancelled);
    }
    private sealed class WaitingHandler : HttpMessageHandler {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            Started.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
    private sealed class CapturingRunner : IProcessRunner {
        public List<(string Executable, IReadOnlyList<string> Arguments)> Calls = [];
        public Queue<OperationResult> Results = [];
        public Task<OperationResult> Result = Task.FromResult(new OperationResult(true, "Done"));
        public Task<OperationResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken token) {
            Calls.Add((executable, arguments));
            return Results.TryDequeue(out var result) ? Task.FromResult(result) : Result;
        }
    }
}
