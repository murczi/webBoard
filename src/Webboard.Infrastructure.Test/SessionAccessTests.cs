namespace Webboard.Infrastructure.Test;

using System.Security.Claims;
using Domain.Interfaces.Services;
using Domain.Model.UserCrudAccess;
using Domain.Services;
using Web.Authentication;

// Page-rendering tests intentionally supply their own claims; production never registers this validator.
internal sealed class PreserveTestClaimsValidator : ISessionAccessValidator {
    public Task<bool> ValidateAsync(ClaimsPrincipal principal, CancellationToken token) => Task.FromResult(true);
}

public sealed class SessionAccessTests {
    [Fact]
    public void DiagnosticsKeepCauseTypesWithoutCopyingCredentialMessages() {
        var error = new InvalidOperationException("Password=private-secret", new HttpRequestException("Bearer private-token"));
        var detail = Web.Services.ExceptionDiagnostics.Describe(error);
        Assert.Contains("InvalidOperationException", detail); Assert.Contains("HttpRequestException", detail);
        Assert.DoesNotContain("private-secret", detail); Assert.DoesNotContain("private-token", detail);
    }

    [Fact]
    public async Task DeletedAccountIsRejectedAndRevokedClaimsAreRemoved() {
        var access = new Access();
        var validator = new SessionAccessValidator(access);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(ClaimTypes.NameIdentifier, "1"), new("access", "Modules:delete")], "test"));
        Assert.False(await validator.ValidateAsync(principal, default));
        access.Grants = [];
        Assert.True(await validator.ValidateAsync(principal, default));
        Assert.False(principal.HasClaim("access", "Modules:delete"));
        access.Grants = [new() { Resource = "Modules", CanOperate = true }];
        Assert.True(await validator.ValidateAsync(principal, default));
        Assert.True(principal.HasClaim("access", "Modules:operations"));
        Assert.True(principal.HasClaim("access", "Modules:read"));
    }

    [Fact]
    public async Task ManagementMutationsRequireCurrentAccessBeforeTouchingRepositories() {
        var access = new Access();
        var modules = new AuthorizedModuleManagementService(new(null!), access);
        var hosts = new AuthorizedHostManagementService(new(null!, null!), access);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => modules.AddAsync(new() { Name = "test" }, 1, "Create"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => modules.UpdateAsync(new() { Name = "test" }, 1, "Update"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => modules.DeleteAsync(1, 1, "Delete"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => hosts.AddAsync(new() { Name = "test", AgentBaseUrl = "https://test" }, 1, "Create"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => hosts.UpdateAsync(new() { Name = "test", AgentBaseUrl = "https://test" }, 1, "Update"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => hosts.DeleteAsync(1, 1, "Delete"));
    }

    private sealed class Access : IResourceAuthorization {
        public IReadOnlyList<UserCrudAccessModel>? Grants;
        public Task<IReadOnlyList<UserCrudAccessModel>?> GetCurrentAsync(int actorId, CancellationToken token) => Task.FromResult(Grants);
        public Task RequireAsync(int actorId, string resource, string action, CancellationToken token) => throw new UnauthorizedAccessException();
    }
}
