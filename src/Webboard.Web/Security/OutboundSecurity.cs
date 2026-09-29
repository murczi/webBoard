namespace Webboard.Web.Security;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;

public sealed class AgentConnection {
    public string BaseUrl { get; set; } = "";
    public string Token { get; set; } = "";
    public bool AllowHttpOverEncryptedTransport { get; set; }
}

public sealed class NetworkPolicy(IConfiguration configuration) {
    public bool IsAllowed(IPAddress address) {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.Equals(IPAddress.Parse("100.100.100.200")) || address.Equals(IPAddress.Parse("168.63.129.16")) ||
            address.Equals(IPAddress.Parse("fd00:ec2::254"))) return false;
        var bytes = address.GetAddressBytes();
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
            address.IsIPv6Multicast || address.IsIPv6LinkLocal ||
            (bytes.Length == 4 && (bytes[0] == 169 && bytes[1] == 254 || bytes[0] >= 224 || bytes[0] == 0)))
            return false;
        var privateAddress = IPAddress.IsLoopback(address) || address.IsIPv6SiteLocal ||
            (bytes.Length == 4 && (bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                                   bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 100 && bytes[1] is >= 64 and <= 127)) ||
            (bytes.Length == 16 && (bytes[0] & 0xfe) == 0xfc);
        if (!privateAddress) return true;
        return (configuration.GetSection("Outbound:AllowedPrivateNetworks").Get<string[]>() ?? [])
            .Any(network => System.Net.IPNetwork.TryParse(network, out var range) && range.Contains(address));
    }

    public async Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) {
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(address => !IsAllowed(address)))
            throw new HttpRequestException("Destination is not permitted by the outbound network policy.");
        return addresses;
    }

    public SocketsHttpHandler CreateHandler() => new() {
        AllowAutoRedirect = false,
        UseProxy = false,
        ConnectCallback = async (context, token) => {
            var addresses = await ResolveAsync(context.DnsEndPoint.Host, token);
            foreach (var address in addresses) {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try {
                    await socket.ConnectAsync(address, context.DnsEndPoint.Port, token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); if (token.IsCancellationRequested) throw; }
            }
            throw new HttpRequestException("Cannot connect to the configured destination.");
        }
    };
}

public sealed class AgentAuthenticationHandler(IConfiguration configuration) : DelegatingHandler {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        var uri = request.RequestUri ?? throw new HttpRequestException("Missing agent endpoint.");
        var profile = (configuration.GetSection("Agents").Get<AgentConnection[]>() ?? [])
            .SingleOrDefault(candidate => Matches(candidate.BaseUrl, uri));
        if (profile is null || profile.Token.Length < 32 ||
            uri.Scheme != "https" && !(uri.Scheme == "http" && profile.AllowHttpOverEncryptedTransport))
            throw new HttpRequestException("Agent security profile is missing or invalid.");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", profile.Token);
        return base.SendAsync(request, cancellationToken);
    }

    public static bool Matches(string baseUrl, Uri uri) =>
        Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var configured) &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment) &&
        configured.Scheme == uri.Scheme && configured.IdnHost == uri.IdnHost && configured.Port == uri.Port &&
        uri.AbsolutePath.StartsWith(configured.AbsolutePath, StringComparison.Ordinal);
}

public static class OutboundSecurityValidation {
    public static void Validate(IConfiguration configuration) {
        var profiles = configuration.GetSection("Agents").Get<AgentConnection[]>() ?? [];
        foreach (var profile in profiles) {
            if (!Uri.TryCreate(profile.BaseUrl, UriKind.Absolute, out var uri) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
                profile.Token.Length < 32 || profile.Token.Any(char.IsWhiteSpace) ||
                uri.Scheme != "https" && !(uri.Scheme == "http" && profile.AllowHttpOverEncryptedTransport))
                throw new InvalidOperationException("Agents entries require a valid base URL, a random token, and HTTPS or an explicit encrypted-transport exception.");
            if (profiles.Count(candidate => AgentAuthenticationHandler.Matches(candidate.BaseUrl, new Uri(profile.BaseUrl.TrimEnd('/') + "/health"))) != 1)
                throw new InvalidOperationException("Agent base URLs must not overlap.");
        }
        foreach (var network in configuration.GetSection("Outbound:AllowedPrivateNetworks").Get<string[]>() ?? [])
            if (!System.Net.IPNetwork.TryParse(network, out _)) throw new InvalidOperationException("Outbound private networks must use CIDR notation.");
    }
}
