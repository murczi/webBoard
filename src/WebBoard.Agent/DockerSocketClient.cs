namespace WebBoard.Agent;

using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

public sealed class DockerSocketClient : IDisposable {
    private readonly HttpClient client;

    public DockerSocketClient(IConfiguration configuration) {
        var socketPath = configuration["Docker:SocketPath"] ?? "/var/run/docker.sock";
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, cancellationToken) => {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try {
                    await socket.ConnectAsync(
                        new UnixDomainSocketEndPoint(socketPath),
                        cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch {
                    socket.Dispose();
                    throw;
                }
            }
        };
        client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://docker"),
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    public async Task<IReadOnlyList<DockerContainerDto>> GetContainersAsync(
        CancellationToken cancellationToken) {
        var containers = await client.GetFromJsonAsync<List<DockerContainerSummary>>(
            "/containers/json?all=true",
            cancellationToken) ?? [];

        return containers
            .Select(container => new DockerContainerDto(
                container.Id,
                container.Names?.FirstOrDefault()?.TrimStart('/') ?? container.Id[..Math.Min(12, container.Id.Length)],
                container.Image,
                container.State,
                container.Status))
            .OrderBy(container => container.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<DockerContainerStatusDto?> GetContainerStatusAsync(
        string containerId,
        CancellationToken cancellationToken) {
        using var response = await client.GetAsync(
            $"/containers/{Uri.EscapeDataString(containerId)}/json",
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        var container = await response.Content.ReadFromJsonAsync<DockerContainerInspect>(
            cancellationToken: cancellationToken);
        if (container?.State is null)
            throw new InvalidOperationException("Docker returned an invalid container response.");

        var healthStatus = container.State.Health?.Status;
        var isHealthy = container.State.Running &&
                        (healthStatus is null || string.Equals(
                            healthStatus,
                            "healthy",
                            StringComparison.OrdinalIgnoreCase));
        var message = healthStatus is null
            ? container.State.Status
            : $"{container.State.Status} (health: {healthStatus})";
        return new DockerContainerStatusDto(
            container.Id,
            container.Name.TrimStart('/'),
            container.State.Status,
            healthStatus,
            isHealthy,
            message);
    }

    public void Dispose() => client.Dispose();

    private sealed record DockerContainerSummary(
        [property: JsonPropertyName("Id")] string Id,
        [property: JsonPropertyName("Names")] IReadOnlyList<string>? Names,
        [property: JsonPropertyName("Image")] string Image,
        [property: JsonPropertyName("State")] string State,
        [property: JsonPropertyName("Status")] string Status);

    private sealed record DockerContainerInspect(
        [property: JsonPropertyName("Id")] string Id,
        [property: JsonPropertyName("Name")] string Name,
        [property: JsonPropertyName("State")] DockerContainerState State);

    private sealed record DockerContainerState(
        [property: JsonPropertyName("Status")] string Status,
        [property: JsonPropertyName("Running")] bool Running,
        [property: JsonPropertyName("Health")] DockerHealth? Health);

    private sealed record DockerHealth(
        [property: JsonPropertyName("Status")] string Status);
}

public sealed record DockerContainerDto(
    string Id,
    string Name,
    string Image,
    string State,
    string Status);

public sealed record DockerContainerStatusDto(
    string Id,
    string Name,
    string State,
    string? HealthStatus,
    bool IsHealthy,
    string Message);
