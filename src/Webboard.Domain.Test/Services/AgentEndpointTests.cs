namespace Webboard.Domain.Test.Services;
using Domain.Model.Hosts;

public sealed class AgentEndpointTests {
    [Fact]
    public void EndpointsPreservePrefixesAndRejectEscapesAndCredentialProfiles() {
        var endpoint = AgentEndpoint.Create(" https://agent.test/api/ ");
        Assert.Equal("https://agent.test/api", endpoint.Value);
        Assert.Equal("https://agent.test/api/health", endpoint.For("health").AbsoluteUri);
        Assert.False(endpoint.Contains(new("https://agent.test/api-other/health")));
        Assert.False(endpoint.Contains(new("https://agent.test:444/api/health")));
        foreach (var path in new[] { "../health", "/health", "https://other.test/health", "%2e%2e/health" })
            Assert.Throws<ArgumentException>(() => endpoint.For(path));
        foreach (var url in new[] { "https://user:secret@agent.test", "https://agent.test?secret=x", "ftp://agent.test", "https://agent.test#fragment" })
            Assert.Throws<ArgumentException>(() => AgentEndpoint.Create(url));
    }
}
