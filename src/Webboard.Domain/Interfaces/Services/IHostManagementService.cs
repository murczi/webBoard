namespace Webboard.Domain.Interfaces.Services;

using Model.Hosts;

public interface IHostManagementService {
    Task<IReadOnlyList<HostModel>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<AgentHealthResult> TestAgentAsync(string agentBaseUrl, CancellationToken cancellationToken = default);
    Task<HostModel> AddAsync(HostModel host, int actorId, string auditComment, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(HostModel host, int actorId, string auditComment, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int hostId, int actorId, string auditComment, CancellationToken cancellationToken = default);
}
