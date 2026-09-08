namespace Webboard.Infrastructure.Test;

using System.IdentityModel.Tokens.Jwt;
using Configuration;
using Domain.Model.UserCrudAccess;
using Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Repositories;
using Web.Authentication;

// Set WEBBOARD_TEST_DATABASE to a disposable PostgreSQL server connection with CREATE DATABASE rights.
// Each test creates and removes its own database; no existing database tables are changed.
public sealed class UserAccessTests : IAsyncLifetime {
    private readonly string databaseName = "webboard_access_test_" + Guid.NewGuid().ToString("N");
    private string connectionString = null!;

    public async Task InitializeAsync() {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("WEBBOARD_TEST_DATABASE"));
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {databaseName}", connection);
        await command.ExecuteNonQueryAsync();
        builder.Database = databaseName;
        connectionString = builder.ConnectionString;
        await using var db = Context();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() {
        await using var connection = new NpgsqlConnection(Environment.GetEnvironmentVariable("WEBBOARD_TEST_DATABASE"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE {databaseName} WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }

    private WebboardDbContext Context() => new(new DbContextOptionsBuilder<WebboardDbContext>()
        .UseNpgsql(connectionString).Options);

    private async Task<int> Register(string name) {
        await using var db = Context();
        var repository = new UserAuthenticationRepository(db);
        Assert.True(await repository.CreateAsync(name, "unused-hash"));
        return (await repository.FindByNameAsync(name))!.Id;
    }

    [PostgresFact]
    public async Task FirstUserGetsAllAccessAndLaterUsersGetNone() {
        var first = await Register("first");
        var second = await Register("second");
        await using var db = Context();
        var grants = await db.UserCrudAccess.Where(x => x.UserId == first).ToListAsync();
        Assert.Equal(5, grants.Count);
        Assert.All(grants, grant => {
            Assert.True(grant.CanRead);
            var writable = grant.Resource is "Users" or "Hosts" or "Modules";
            Assert.Equal(writable, grant.CanCreate);
            Assert.Equal(writable, grant.CanUpdate);
            Assert.Equal(writable, grant.CanDelete);
        });
        Assert.False(await db.UserCrudAccess.AnyAsync(x => x.UserId == second));
    }

    [PostgresFact]
    public async Task ConcurrentRegistrationsProduceExactlyOneAdministrator() {
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Register($"user{i}")));
        await using var db = Context();
        Assert.Equal(8, await db.Users.CountAsync());
        Assert.Equal(1, await db.UserCrudAccess.Select(x => x.UserId).Distinct().CountAsync());
        Assert.Equal(5, await db.UserCrudAccess.CountAsync());
    }

    [PostgresFact]
    public async Task UnprivilegedUserCannotGrantAccessOrDeleteUsers() {
        var admin = await Register("admin");
        var user = await Register("user");
        await using var db = Context();
        var service = new UserCrudAccessService(new UserCrudAccessRepository(db));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAccessAsync(
            user, [new() { Resource = "Users", CanUpdate = true }], user, "Self promotion"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DeleteUserAsync(admin, user, "Delete admin"));
        Assert.False(await db.UserCrudAccess.AnyAsync(x => x.UserId == user));
        Assert.False((await db.Users.FindAsync(admin))!.DeletionFlag);
    }

    [PostgresFact]
    public async Task WritesImplyReadAndReadOnlyResourcesRejectWrites() {
        var admin = await Register("admin");
        var user = await Register("user");
        await using var db = Context();
        var service = new UserCrudAccessService(new UserCrudAccessRepository(db));
        await service.SaveAccessAsync(user, [
            new() { Resource = "Users", CanUpdate = true },
            new() { Resource = "Hosts", CanCreate = true },
            new() { Resource = "Modules", CanDelete = true },
            new() { Resource = "ModuleTypes", CanRead = true, CanUpdate = true },
            new() { Resource = "AuditLogs", CanRead = true, CanDelete = true }
        ], admin, "Grant access");
        var grants = await service.GetAccessAsync(user);
        Assert.All(grants, grant => Assert.True(grant.CanRead));
        Assert.All(grants.Where(x => x.IsReadOnlyResource), grant => {
            Assert.False(grant.CanCreate);
            Assert.False(grant.CanUpdate);
            Assert.False(grant.CanDelete);
        });
        // Update permission delegates permission management, but does not grant deletion.
        db.ChangeTracker.Clear();
        await service.SaveAccessAsync(user, [new() { Resource = "Users", CanUpdate = true }], user, "Update own grants");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DeleteUserAsync(admin, user, "Delete admin"));
    }

    [PostgresFact]
    public async Task RevokedUpdatePermissionRejectsPreviouslyAuthorizedActor() {
        var admin = await Register("admin");
        var user = await Register("user");
        await using var db = Context();
        var service = new UserCrudAccessService(new UserCrudAccessRepository(db));
        await service.SaveAccessAsync(user, [new() { Resource = "Users", CanUpdate = true }], admin, "Delegate");
        var sessions = new JwtSessionService(new UserAuthenticationRepository(db), Options.Create(new JwtOptions {
            SigningKey = new string('x', 64)
        }));
        var token = new JwtSecurityTokenHandler().ReadJwtToken((await sessions.RefreshTokenAsync("user"))!);
        Assert.Contains(token.Claims, claim => claim.Value == UserAccess.Update);
        Assert.Contains(token.Claims, claim => claim.Value == UserAccess.Read);
        db.ChangeTracker.Clear();
        await service.SaveAccessAsync(user, [], admin, "Revoke");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAccessAsync(
            user, [new() { Resource = "Users", CanUpdate = true }], user, "Stale session"));
    }

    [PostgresFact]
    public async Task DeletePermissionAllowsDeletingOthersButNotSelf() {
        var admin = await Register("admin");
        var user = await Register("user");
        await using var db = Context();
        var service = new UserCrudAccessService(new UserCrudAccessRepository(db));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DeleteUserAsync(admin, admin, "Delete self"));
        Assert.True(await service.DeleteUserAsync(user, admin, "Delete other"));
        Assert.Null(await new UserAuthenticationRepository(db).FindByNameAsync("user"));
    }
}

public sealed class PostgresFactAttribute : FactAttribute {
    public PostgresFactAttribute() {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBBOARD_TEST_DATABASE")))
            Skip = "Set WEBBOARD_TEST_DATABASE to run PostgreSQL integration tests.";
    }
}
