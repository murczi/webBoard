namespace Webboard.Domain.Test.Services;

using Domain.Interfaces.Repositories;
using Domain.Interfaces.Services;
using Domain.Model.Hosts;
using Domain.Services;

public class HostManagementServiceTests {
    [Fact]
    public async Task AddAsync_AddsHostWhenAgentIsHealthy() {
        var repository = new FakeHostRepository();
        var service = new HostManagementService(
            repository,
            new FakeHealthChecker(new AgentHealthResult(true, "Agent is healthy.")));
        var host = NewHost(" http://agent.local:5080/ ");

        await service.AddAsync(host);

        Assert.Single(repository.Hosts);
        Assert.Equal("http://agent.local:5080", host.AgentBaseUrl);
        Assert.NotEqual(default, host.DateCreated);
    }

    [Fact]
    public async Task AddAsync_RejectsHostWhenAgentIsUnhealthy() {
        var repository = new FakeHostRepository();
        var service = new HostManagementService(
            repository,
            new FakeHealthChecker(new AgentHealthResult(false, "Agent could not be reached.")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AddAsync(NewHost("http://agent.local:5080")));

        Assert.Equal("Agent could not be reached.", exception.Message);
        Assert.Empty(repository.Hosts);
    }

    [Fact]
    public async Task UpdateAsync_RejectsConfigurationWhenAgentIsUnhealthy() {
        var repository = new FakeHostRepository();
        var service = new HostManagementService(
            repository,
            new FakeHealthChecker(new AgentHealthResult(false, "Agent timed out.")));
        var host = NewHost("http://agent.local:5080");
        host.Id = 7;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(host));

        Assert.Empty(repository.Hosts);
    }

    [Theory]
    [InlineData("ftp://agent.local")]
    [InlineData("/relative-agent")]
    [InlineData("http://user:password@agent.local")]
    [InlineData("http://agent.local?token=secret")]
    public async Task AddAsync_RejectsInvalidAgentBaseUrl(string agentBaseUrl) {
        var service = new HostManagementService(
            new FakeHostRepository(),
            new FakeHealthChecker(new AgentHealthResult(true, "Agent is healthy.")));

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync(NewHost(agentBaseUrl)));
    }

    private static HostModel NewHost(string agentBaseUrl) => new()
    {
        Name = "Test host",
        AgentBaseUrl = agentBaseUrl,
        IsEnabled = true
    };

    private sealed class FakeHealthChecker(AgentHealthResult result) : IAgentHealthChecker {
        public Task<AgentHealthResult> CheckAsync(
            string agentBaseUrl,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class FakeHostRepository : IHostRepository {
        public List<HostModel> Hosts { get; } = [];

        public Task<IReadOnlyList<HostModel>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HostModel>>(Hosts);

        public Task<bool> NameExistsAsync(
            string name,
            int? excludingHostId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Hosts.Any(host =>
                host.Id != excludingHostId &&
                string.Equals(host.Name, name, StringComparison.OrdinalIgnoreCase)));

        public Task AddAsync(HostModel host, CancellationToken cancellationToken = default) {
            host.Id = Hosts.Count + 1;
            Hosts.Add(host);
            return Task.CompletedTask;
        }

        public Task<bool> UpdateAsync(HostModel host, CancellationToken cancellationToken = default) {
            var index = Hosts.FindIndex(item => item.Id == host.Id);
            if (index < 0)
                return Task.FromResult(false);
            Hosts[index] = host;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(int hostId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Hosts.RemoveAll(host => host.Id == hostId) > 0);
    }
}
