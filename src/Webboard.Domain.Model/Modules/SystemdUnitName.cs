namespace Webboard.Domain.Model.Modules;

public static class SystemdUnitName {
    public const int MaximumLength = 256;
    public static bool IsValid(string? value) => value is { Length: > 8 and <= MaximumLength } &&
        (char.IsAsciiLetterOrDigit(value[0]) || value[0] == '_') &&
        value.EndsWith(".service", StringComparison.Ordinal) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.' or '@' or ':' or '\\' or '-');
}
