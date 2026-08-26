namespace Webboard.Domain.Services;

using Interfaces.Repositories;
using Interfaces.Services;
using Model.Hosts;

public class HostManagementService(IHostRepository repository, IAgentHealthChecker healthChecker)
    : IHostManagementService {
    public Task<IReadOnlyList<HostModel>> GetAllAsync(CancellationToken cancellationToken = default) =>
        repository.GetAllAsync(cancellationToken);

    public Task<AgentHealthResult> TestAgentAsync(string agentBaseUrl, CancellationToken cancellationToken = default) =>
        healthChecker.CheckAsync(agentBaseUrl, cancellationToken);

    public async Task<HostModel> AddAsync(HostModel host, CancellationToken cancellationToken = default) {
        Normalize(host);
        if (await repository.NameExistsAsync(host.Name, cancellationToken: cancellationToken))
            throw new ArgumentException("A host with this name already exists.", nameof(host));

        await RequireHealthyAgentAsync(host.AgentBaseUrl, cancellationToken);
        host.DateCreated = DateTime.UtcNow;
        await repository.AddAsync(host, cancellationToken);
        return host;
    }

    public async Task<bool> UpdateAsync(HostModel host, CancellationToken cancellationToken = default) {
        Normalize(host);
        if (await repository.NameExistsAsync(host.Name, host.Id, cancellationToken))
            throw new ArgumentException("A host with this name already exists.", nameof(host));

        await RequireHealthyAgentAsync(host.AgentBaseUrl, cancellationToken);
        return await repository.UpdateAsync(host, cancellationToken);
    }

    public Task<bool> DeleteAsync(int hostId, CancellationToken cancellationToken = default) =>
        repository.DeleteAsync(hostId, cancellationToken);

    private async Task RequireHealthyAgentAsync(string agentBaseUrl, CancellationToken cancellationToken) {
        var health = await healthChecker.CheckAsync(agentBaseUrl, cancellationToken);
        if (!health.IsHealthy)
            throw new InvalidOperationException(health.Message);
    }

    private static void Normalize(HostModel host) {
        host.Name = host.Name.Trim();
        host.AgentBaseUrl = NormalizeAgentBaseUrl(host.AgentBaseUrl);
        if (host.Name.Length is 0 or > 100)
            throw new ArgumentException("Host name must be between 1 and 100 characters.", nameof(host));
    }

    private static string NormalizeAgentBaseUrl(string value) {
        var candidate = value.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException(
                "Agent base URL must be an absolute HTTP or HTTPS URL without credentials, a query, or a fragment.",
                nameof(value));

        var normalized = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        if (normalized.Length > 2048)
            throw new ArgumentException("Agent base URL cannot exceed 2048 characters.", nameof(value));
        return normalized;
    }
}
