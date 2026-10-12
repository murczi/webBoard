namespace Webboard.Infrastructure.Test;

using System.Net.Sockets;
using System.Text;
using Agent;

public sealed class DockerRediscoveryTests {
    [Fact]
    public async Task StableNameRecoversAfterStopAndRecreationAndControlsTheReplacement() {
        var directory = Path.Combine(Path.GetTempPath(), "webboard-rediscovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "docker.sock");
        try {
            using var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            server.Bind(new UnixDomainSocketEndPoint(path));
            server.Listen(1);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var responses = new[] {
                ("GET /containers/amp-server/json HTTP/1.1", "200 OK", Inspect("old-id", true)),
                ("GET /containers/amp-server/json HTTP/1.1", "200 OK", Inspect("old-id", false)),
                ("GET /containers/amp-server/json HTTP/1.1", "404 Not Found", "{}"),
                ("GET /containers/amp-server/json HTTP/1.1", "200 OK", Inspect("replacement-id", true)),
                ("GET /containers/amp-server/json HTTP/1.1", "200 OK", Inspect("replacement-id", true)),
                ("POST /containers/replacement-id/restart?t=10 HTTP/1.1", "204 No Content", "")
            };
            var serve = Task.Run(async () => {
                foreach (var (request, status, body) in responses) {
                    using var socket = await server.AcceptAsync(deadline.Token);
                    using var stream = new NetworkStream(socket);
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    Assert.Equal(request, await reader.ReadLineAsync(deadline.Token));
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(deadline.Token))) { }
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}"), deadline.Token);
                }
            });
            var config = AgentSecurityTests.Config(new() {
                ["Docker:SocketPath"] = path,
                ["Controls:Docker:0"] = "amp-server"
            });
            using var docker = new DockerSocketClient(config);
            Assert.True((await docker.GetContainerStatusAsync("amp-server", deadline.Token))!.IsHealthy);
            Assert.False((await docker.GetContainerStatusAsync("amp-server", deadline.Token))!.IsHealthy);
            Assert.Null(await docker.GetContainerStatusAsync("amp-server", deadline.Token));
            var replacement = await docker.GetContainerStatusAsync("amp-server", deadline.Token);
            Assert.True(replacement!.IsHealthy);
            Assert.Equal("replacement-id", replacement.Id);
            Assert.True((await new AgentOperations(config, docker, new UnusedRunner()).ExecuteAsync(
                new(Guid.NewGuid(), 1, "Docker", "amp-server", "restart"), deadline.Token)).Success);
            await serve;
        } finally { Directory.Delete(directory, true); }
    }

    private static string Inspect(string id, bool running) =>
        System.Text.Json.JsonSerializer.Serialize(new {
            Id = id,
            Name = "/amp-server",
            State = new { Status = running ? "running" : "exited", Running = running }
        });

    private sealed class UnusedRunner : IProcessRunner {
        public Task<OperationResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken token) =>
            throw new InvalidOperationException("Docker controls must use the Docker socket.");
    }
}
