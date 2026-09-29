namespace Webboard.Infrastructure.Test;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using WebBoard.Agent;
using Web.Security;
public sealed class AgentSecurityTests {
    private const string Token = "test-agent-secret-0123456789abcdef0123456789";
    [Fact]
    public void MissingOrWeakKeysFailClosed() {
        Assert.Throws<InvalidOperationException>(() => new AgentSecurity(Config([])));
        Assert.Throws<InvalidOperationException>(() => new AgentSecurity(Config(new() { ["Security:Tokens:0"] = "short" })));
    }
    [Theory]
    [InlineData("", false)] [InlineData("Bearer wrong", false)] [InlineData("Basic ignored", false)]
    [InlineData("Bearer " + Token, true)]
    public void CredentialValidation(string header, bool expected) =>
        Assert.Equal(expected, new AgentSecurity(Config(new() { ["Security:Tokens:0"] = Token })).Authenticate(header));
    [Fact]
    public async Task EndpointsRequireAuthentication() {
        await using var factory = new WebApplicationFactory<AgentSecurity>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Testing").ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Security:Tokens:0"] = Token })));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/docker/containers")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }
    [Theory]
    [InlineData("127.0.0.1", false)] [InlineData("10.0.0.1", true)] [InlineData("169.254.169.254", false)]
    [InlineData("::ffff:169.254.169.254", false)] [InlineData("::1", false)] [InlineData("224.0.0.1", false)]
    public void NetworkPolicyAppliesToResolvedAddresses(string ip, bool allowed) {
        var policy = new NetworkPolicy(Config(new() { ["Outbound:AllowedPrivateNetworks:0"] = "10.0.0.0/8" }));
        Assert.Equal(allowed, policy.IsAllowed(IPAddress.Parse(ip)));
    }
    [Fact]
    public async Task AgentCredentialsAreBoundToTheirProfileAndHttpRequiresExplicitOptIn() {
        var config = Config(new() { ["Agents:0:BaseUrl"] = "https://first.test/agent", ["Agents:0:Token"] = Token,
            ["Agents:1:BaseUrl"] = "https://second.test", ["Agents:1:Token"] = Token + "-second" });
        var captured = new CaptureHandler();
        using var handler = new AgentAuthenticationHandler(config) { InnerHandler = captured };
        using var client = new HttpClient(handler);
        await client.GetAsync("https://first.test/agent/health"); Assert.Equal(Token, captured.Token);
        await client.GetAsync("https://second.test/health"); Assert.Equal(Token + "-second", captured.Token);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://first.test/elsewhere"));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://unconfigured.test/health"));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("http://second.test/health"));
        Assert.Equal(2, captured.Calls);
    }
    private sealed class CaptureHandler : HttpMessageHandler {
        public string? Token; public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            Calls++; Token = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
    internal static IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
