namespace Webboard.Web.Authentication;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Domain.Interfaces.Repositories;
using Domain.Model.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

public sealed class JwtSessionService(
    IUserAuthenticationRepository users,
    IOptions<JwtOptions> options) {
    public const string AccessClaimType = "access";
    public const string RememberMeClaimType = "remember_me";

    private readonly JwtOptions jwtOptions = options.Value;
    private readonly PasswordHasher<UserLoginModel> passwordHasher = new();
    private readonly string dummyPasswordHash = new PasswordHasher<UserLoginModel>()
        .HashPassword(null!, "invalid-password");

    public async Task<string?> CreateTokenAsync(
        string userName,
        string password,
        bool rememberMe = false,
        CancellationToken cancellationToken = default) {
        var user = await users.FindByNameAsync(userName.Trim(), cancellationToken);
        if (user is null) {
            passwordHasher.VerifyHashedPassword(null!, dummyPasswordHash, password);
            return null;
        }

        if (!VerifyPassword(user, password))
            return null;

        return CreateToken(user, rememberMe);
    }

    public async Task<string?> RefreshTokenAsync(
        string userName,
        bool rememberMe = false,
        CancellationToken cancellationToken = default) {
        var user = await users.FindByNameAsync(userName.Trim(), cancellationToken);
        return user is null ? null : CreateToken(user, rememberMe);
    }

    private string CreateToken(UserLoginModel user, bool rememberMe) {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Name),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(RememberMeClaimType, rememberMe.ToString().ToLowerInvariant())
        };

        foreach (var access in user.Access) {
            AddAccessClaim(claims, access.Resource, "create", access.CanCreate);
            AddAccessClaim(claims, access.Resource, "read", access.CanRead || access.CanCreate || access.CanUpdate || access.CanDelete);
            AddAccessClaim(claims, access.Resource, "update", access.CanUpdate);
            AddAccessClaim(claims, access.Resource, "delete", access.CanDelete);
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: jwtOptions.Issuer,
            audience: jwtOptions.Audience,
            claims: claims,
            notBefore: now,
            expires: now.Add(GetLifetime(rememberMe)),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<string?> RegisterAsync(
        string userName,
        string password,
        bool rememberMe = false,
        CancellationToken cancellationToken = default) {
        var normalizedUserName = userName.Trim();
        var passwordHash = HashPassword(password);
        if (!await users.CreateAsync(
                normalizedUserName,
                passwordHash,
                cancellationToken))
            return null;

        return await CreateTokenAsync(
            normalizedUserName,
            password,
            rememberMe,
            cancellationToken);
    }

    public CookieOptions CreateCookieOptions(
        bool rememberMe = false,
        bool secure = true) {
        var options = new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            IsEssential = true,
            Path = "/"
        };

        if (rememberMe) {
            var lifetime = GetLifetime(true);
            options.MaxAge = lifetime;
            options.Expires = DateTimeOffset.UtcNow.Add(lifetime);
        }

        return options;
    }

    private static void AddAccessClaim(
        ICollection<Claim> claims,
        string resource,
        string operation,
        bool allowed) {
        if (allowed)
            claims.Add(new Claim(AccessClaimType, $"{resource}:{operation}"));
    }

    private bool VerifyPassword(UserLoginModel user, string password) {
        const string pbkdf2Prefix = "PBKDF2-SHA256$";
        if (user.PasswordHash.StartsWith(pbkdf2Prefix, StringComparison.Ordinal))
            return VerifyPbkdf2Sha256(user.PasswordHash, password);

        try {
            return passwordHasher.VerifyHashedPassword(
                       user,
                       user.PasswordHash,
                       password) != PasswordVerificationResult.Failed;
        }
        catch (FormatException) {
            return false;
        }
    }

    private static bool VerifyPbkdf2Sha256(string storedHash, string password) {
        var parts = storedHash.Split('$');
        if (parts.Length != 4 ||
            !int.TryParse(parts[1], out var iterations) ||
            iterations is <= 0 or > 1_000_000)
            return false;

        try {
            var salt = Convert.FromBase64String(parts[2]);
            var expectedHash = Convert.FromBase64String(parts[3]);
            if (salt.Length == 0 || expectedHash.Length == 0)
                return false;

            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException) {
            return false;
        }
    }

    private static string HashPassword(string password) {
        const int iterations = 100_000;
        const int saltSize = 16;
        const int hashSize = 32;

        var salt = RandomNumberGenerator.GetBytes(saltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            hashSize);

        return $"PBKDF2-SHA256${iterations}${Convert.ToBase64String(salt)}$" +
               Convert.ToBase64String(hash);
    }

    private TimeSpan GetLifetime(bool rememberMe) =>
        rememberMe
            ? TimeSpan.FromDays(jwtOptions.RememberMeDays)
            : TimeSpan.FromHours(jwtOptions.LifetimeHours);
}
