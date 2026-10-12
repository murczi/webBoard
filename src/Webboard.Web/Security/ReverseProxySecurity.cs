namespace Webboard.Web.Security;

using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

public static class ReverseProxySecurity {
    public static void Configure(ForwardedHeadersOptions options, IConfiguration configuration) {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        foreach (var value in configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
            options.KnownProxies.Add(IPAddress.Parse(value));
        foreach (var value in configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [])
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(value));
    }
}
