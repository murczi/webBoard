namespace Webboard.Domain.Interfaces.Services;

using Model.Modules;

public interface ISystemdAgentClient {
    Task<IReadOnlyList<SystemdServiceModel>> GetServicesAsync(
        string agentBaseUrl,
        CancellationToken cancellationToken = default);

    Task<ModuleHealthResult> CheckServiceAsync(
        string agentBaseUrl,
        string serviceName,
        CancellationToken cancellationToken = default);
}
