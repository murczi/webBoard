namespace Webboard.Domain.Interfaces.Services;

using Model.Hosts;

public interface IAgentHealthChecker {
    Task<AgentHealthResult> CheckAsync(string agentBaseUrl, CancellationToken cancellationToken = default);
}
