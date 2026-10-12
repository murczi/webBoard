namespace Webboard.Domain.Model.Modules;

public static class ServerAddress {
    public static bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 253 &&
        !value.Any(char.IsWhiteSpace) && !value.Contains('/') && !value.Contains('\\') &&
        Uri.CheckHostName(value) != UriHostNameType.Unknown;
}
