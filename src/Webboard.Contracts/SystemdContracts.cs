namespace Webboard.Contracts;

public sealed record SystemdServiceDto(
    string Name,
    string Description,
    string LoadState,
    string ActiveState,
    string SubState);

public sealed record SystemdServiceStatusDto(
    string Name,
    string Description,
    string ActiveState,
    string SubState,
    [property: System.Text.Json.Serialization.JsonRequired] bool IsHealthy,
    string Message);
