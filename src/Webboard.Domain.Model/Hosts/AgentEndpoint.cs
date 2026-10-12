namespace Webboard.Domain.Model.Hosts;

public sealed class AgentEndpoint {
    private AgentEndpoint(Uri uri) {
        Value = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        BaseUri = new Uri(Value + "/");
    }
    public string Value { get; }
    public Uri BaseUri { get; }
    public static AgentEndpoint Create(string? value) {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Agent base URL must be an absolute HTTP or HTTPS URL without credentials, a query, or a fragment.");
        var endpoint = new AgentEndpoint(uri);
        if (endpoint.Value.Length > 2048) throw new ArgumentException("Agent base URL cannot exceed 2048 characters.");
        return endpoint;
    }
    public Uri For(string relativePath) {
        if (relativePath.StartsWith('/') || !Uri.TryCreate(relativePath, UriKind.Relative, out _) ||
            relativePath.Split('?')[0].Split('/').Any(segment => Uri.UnescapeDataString(segment) is "." or ".."))
            throw new ArgumentException("Agent endpoints must remain relative to their configured base URL.");
        var uri = new Uri(BaseUri, relativePath);
        if (!Contains(uri)) throw new ArgumentException("Endpoint escapes the configured agent base URL.");
        return uri;
    }
    public bool Contains(Uri uri) => string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment) &&
        BaseUri.Scheme == uri.Scheme && BaseUri.IdnHost == uri.IdnHost && BaseUri.Port == uri.Port &&
        uri.AbsolutePath.StartsWith(BaseUri.AbsolutePath, StringComparison.Ordinal);
}
