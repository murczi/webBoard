namespace WebBoard.Agent;

using System.Security.Cryptography;
using System.Text;

public sealed class AgentSecurity {
    private readonly byte[][] hashes;
    public AgentSecurity(IConfiguration configuration) {
        var keys = configuration.GetSection("Security:Tokens").Get<string[]>() ?? [];
        if (keys.Length == 0 || keys.Any(key => Encoding.UTF8.GetByteCount(key) < 32))
            throw new InvalidOperationException("Security:Tokens must contain random credentials of at least 32 bytes.");
        hashes = keys.Select(key => SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToArray();
    }
    public bool Authenticate(string authorization) {
        if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal) || authorization.Length > 4096) return false;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(authorization[7..]));
        var valid = false;
        foreach (var expected in hashes) valid |= CryptographicOperations.FixedTimeEquals(expected, hash);
        return valid;
    }
}
