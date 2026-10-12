namespace Webboard.Contracts;
using System.Text;

public static class AgentToken {
    public const int MinimumBytes = 32;
    public static bool IsValid(string? token) => !string.IsNullOrEmpty(token) &&
        Encoding.UTF8.GetByteCount(token) >= MinimumBytes && !token.Any(char.IsWhiteSpace);
}
