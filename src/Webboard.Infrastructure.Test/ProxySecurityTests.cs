namespace Webboard.Infrastructure.Test;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Web.Authentication;
using Web.Security;

public sealed class ProxySecurityTests {
    [Theory]
    [InlineData("Production", false, true)]
    [InlineData("Development", false, false)]
    [InlineData("Development", true, true)]
    public void CookieSecurityDependsOnEnvironmentAndOriginalScheme(string environment, bool https, bool secure) {
        var context = new DefaultHttpContext {
            RequestServices = new ServiceCollection().AddSingleton<IHostEnvironment>(new Environment(environment)).BuildServiceProvider()
        };
        context.Request.Scheme = https ? "https" : "http";
        Assert.Equal(secure, SessionCookiePolicy.RequiresSecure(context));
    }
    [Fact]
    public void ProxyTrustIsExplicitAndDoesNotTrustForwardedHost() {
        var options = new ForwardedHeadersOptions();
        ReverseProxySecurity.Configure(options, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ReverseProxy:KnownNetworks:0"] = "172.30.0.0/16"
        }).Build());
        Assert.Contains(options.KnownIPNetworks, x => x.Contains(System.Net.IPAddress.Parse("172.30.0.2")));
        Assert.DoesNotContain(options.KnownIPNetworks, x => x.Contains(System.Net.IPAddress.Parse("10.10.10.1")));
        Assert.False(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedHost));
    }
    private sealed class Environment(string name) : IHostEnvironment {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
