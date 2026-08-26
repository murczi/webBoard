namespace Webboard.Domain.Interfaces.Services;

using Model.Modules;

public interface IDockerAgentClient {
    Task<IReadOnlyList<DockerContainerModel>> GetContainersAsync(
        string agentBaseUrl,
        CancellationToken cancellationToken = default);

    Task<ModuleHealthResult> CheckContainerAsync(
        string agentBaseUrl,
        string containerId,
        CancellationToken cancellationToken = default);
}
