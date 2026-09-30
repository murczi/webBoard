namespace Webboard.Infrastructure.Test;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
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

public sealed class ModuleManagementPageTests {
    private const string SigningKey = "module-page-tests-signing-key-not-for-production";

    [Theory]
    [InlineData("Minecraft", "MinecraftServerAddress", "MinecraftServerPort", "25566")]
    [InlineData("Steam", "SteamServerAddress", "SteamQueryPort", "27017")]
    public async Task FailedCreationPreservesSubmittedServerDetails(
        string kind, string addressField, string portField, string port) {
        await using var factory = new PageFactory().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Testing").ConfigureServices(services =>
                services.AddSingleton<IModuleManagementService>(new Modules(kind))));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        var claims = new[] {
            new Claim(JwtSessionService.AccessClaimType, ModuleAccess.Read),
            new Claim(JwtSessionService.AccessClaimType, ModuleAccess.Create),
            new Claim(ClaimTypes.NameIdentifier, "1")
        };
        var token = new JwtSecurityToken("Webboard", "Webboard", claims,
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Add("Cookie", JwtOptions.CookieName + "=" + new JwtSecurityTokenHandler().WriteToken(token));
        var initialHtml = await client.GetStringAsync("/ModuleManagement");
        var verificationToken = Regex.Match(initialHtml,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(verificationToken);

        var response = await client.PostAsync("/ModuleManagement?handler=Add", new FormUrlEncodedContent(
            new Dictionary<string, string> {
                ["__RequestVerificationToken"] = verificationToken,
                ["Module.Name"] = "Rejected module",
                ["Module.TypeId"] = "1",
                [$"Module.{addressField}"] = "server.example.com",
                [$"Module.{portField}"] = port,
                ["Module.SteamQueryPlayers"] = "true",
                ["Module.IsEnabled"] = "true"
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Fixture rejected creation.", html);
        Assert.Contains("value=\"server.example.com\"", InputTag(html, addressField));
        Assert.Contains($"value=\"{port}\"", InputTag(html, portField));
        Assert.Contains("\"retry\":\"Add\"", html);
        if (kind == "Steam") Assert.Contains("checked=\"checked\"", InputTag(html, "SteamQueryPlayers"));
    }

    private static string InputTag(string html, string field) =>
        Regex.Match(html, $"<input[^>]*id=\"Module_{field}\"[^>]*>").Value;

    private sealed class PageFactory : WebApplicationFactory<ModuleManagementModel> {
        protected override IHost CreateHost(IHostBuilder builder) {
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?> {
                ["Authentication:Jwt:SigningKey"] = SigningKey,
                ["ConnectionStrings:WebboardDatabase"] = "Host=unused;Database=unused",
                ["Monitoring:Enabled"] = "false"
            }));
            return base.CreateHost(builder);
        }
    }

    private sealed class Modules(string kind) : IModuleManagementService {
        public Task<IReadOnlyList<ModuleModel>> GetAllAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<ModuleModel>>([]);
        public Task<IReadOnlyList<ModuleOptionModel>> GetHostsAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<ModuleOptionModel>>([]);
        public Task<IReadOnlyList<ModuleOptionModel>> GetTypesAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<ModuleOptionModel>>([new(1, kind)]);
        public Task<ModuleModel> AddAsync(ModuleModel model, int actor, string comment, CancellationToken token = default) => throw new ArgumentException("Fixture rejected creation.");
        public Task<bool> UpdateAsync(ModuleModel model, int actor, string comment, CancellationToken token = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(int id, int actor, string comment, CancellationToken token = default) => throw new NotSupportedException();
    }
}
