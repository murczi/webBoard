namespace Webboard.Contracts;

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
    [property: System.Text.Json.Serialization.JsonRequired] bool IsHealthy,
    string Message);
