namespace Webboard.Infrastructure.Test;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using Domain.Interfaces.Services;
using Domain.Model.Modules;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Web.Authentication;
using Web.Pages;

public sealed class OperationsPageTests {
    private const string SigningKey = "operations-page-tests-signing-key-not-for-production";

    [Theory]
    [InlineData("Docker", "Up", "Down", false)]
    [InlineData("Systemd", "Start", "Stop", true)]
    public async Task RendersOnlyFixedButtonsForTheModuleType(string kind, string startLabel, string stopLabel, bool unitFiles) {
        await using var factory = Factory(kind);
        using var client = Client(factory, ModuleAccess.Read, ModuleAccess.Operations);
        var response = await client.GetAsync("/Operations?moduleId=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains($">{startLabel}</button>", html);
        Assert.Contains($">{stopLabel}</button>", html);
        Assert.Contains(">Restart</button>", html);
        Assert.Equal(unitFiles, html.Contains(">Enable</button>"));
        Assert.Equal(unitFiles, html.Contains(">Disable</button>"));
        Assert.Contains("name=\"operation\" value=\"start\"", html);
        Assert.Contains("name=\"operation\" value=\"stop\"", html);
        Assert.DoesNotContain("commandBindingId", html);
        Assert.DoesNotContain("Predefined commands", html);
        Assert.DoesNotContain("Associate", html);
        Assert.Contains("name=\"__RequestVerificationToken\"", html);
    }

    [Theory]
    [InlineData(ModuleAccess.Read)]
    [InlineData(ModuleAccess.Update)]
    [InlineData(ModuleAccess.Operations)]
    public async Task PageRequiresBothReadAndOperations(string grant) {
        await using var factory = Factory("Docker");
        using var client = Client(factory, grant);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Operations?moduleId=1")).StatusCode);
    }

    [Theory]
    [InlineData("Http", true)] [InlineData("Docker", false)]
    public async Task UnsupportedAndDisabledModulesHaveNoOperationsPage(string kind, bool enabled) {
        await using var factory = Factory(kind, enabled);
        using var client = Client(factory, ModuleAccess.Read, ModuleAccess.Operations);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Operations?moduleId=1")).StatusCode);
    }

    [Fact]
    public async Task UnavailableCapabilitiesShowNoButtons() {
        await using var factory = Factory("Docker", allowed: []);
        using var client = Client(factory, ModuleAccess.Read, ModuleAccess.Operations);
        var html = await client.GetStringAsync("/Operations?moduleId=1");
        Assert.Contains("No service controls are currently available", html);
        Assert.DoesNotContain("class=\"operation-form\"", html);
    }

    private static WebApplicationFactory<OperationsModel> Factory(string kind, bool enabled = true, string[]? allowed = null) =>
        new PageFactory().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Testing")
                .ConfigureServices(services => {
                    services.AddSingleton<IModuleManagementService>(new Modules(new() { Id = 1, Name = "Test module", HostName = "Test host", TypeName = kind, IsEnabled = enabled }));
                    services.AddSingleton<IModuleOperations>(new Operations(allowed ?? ["start", "stop", "restart", "enable", "disable"]));
                }));

    private static HttpClient Client(WebApplicationFactory<OperationsModel> factory, params string[] grants) {
        var claims = grants.Select(grant => new Claim(JwtSessionService.AccessClaimType, grant))
            .Append(new Claim(ClaimTypes.NameIdentifier, "1"));
        var token = new JwtSecurityToken("Webboard", "Webboard", claims, expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256));
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Cookie", JwtOptions.CookieName + "=" + new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    private sealed class PageFactory : WebApplicationFactory<OperationsModel> {
        protected override IHost CreateHost(IHostBuilder builder) {
            // Host configuration is available to startup validation before Build().
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?> {
                ["Authentication:Jwt:SigningKey"] = SigningKey,
                ["ConnectionStrings:WebboardDatabase"] = "Host=unused;Database=unused",
                ["Monitoring:Enabled"] = "false"
            }));
            return base.CreateHost(builder);
        }
    }

    private sealed class Operations(string[] allowed) : IModuleOperations {
        public Task<IReadOnlyList<string>> AllowedServiceOperationsAsync(ModuleModel module, CancellationToken token) => Task.FromResult<IReadOnlyList<string>>(allowed);
        public Task<ModuleOperationResult> ExecuteAsync(int actorId, ModuleOperationRequest request, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Modules(ModuleModel module) : IModuleManagementService {
        public Task<IReadOnlyList<ModuleModel>> GetAllAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<ModuleModel>>([module]);
        public Task<IReadOnlyList<ModuleOptionModel>> GetHostsAsync(CancellationToken token = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModuleOptionModel>> GetTypesAsync(CancellationToken token = default) => throw new NotSupportedException();
        public Task<ModuleModel> AddAsync(ModuleModel model, int actor, string comment, CancellationToken token = default) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(ModuleModel model, int actor, string comment, CancellationToken token = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(int id, int actor, string comment, CancellationToken token = default) => throw new NotSupportedException();
    }
}
