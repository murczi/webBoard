namespace Webboard.Domain.Model.Modules;

public sealed record DockerContainerModel(
    string Id,
    string Name,
    string Image,
    string State,
    string Status);
