namespace Webboard.Domain.Model.Modules;

public sealed record SystemdServiceModel(
    string Name,
    string Description,
    string LoadState,
    string ActiveState,
    string SubState);
