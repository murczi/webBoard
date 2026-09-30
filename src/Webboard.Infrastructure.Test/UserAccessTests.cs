namespace Webboard.Infrastructure.Test;

using System.IdentityModel.Tokens.Jwt;
using Configuration;
using Configuration.Entities;
using Domain.Model.AuditLogs;
using Domain.Model.Hosts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
    public async Task AuditSearchFiltersBeforePagingAndIncludesDeletedHistory() {
        var actor = await Register("audit-actor");
        var otherActor = await Register("other-actor");
        await using var db = Context();
        var host = new HostEntity { Name = "retired-host", AgentBaseUrl = "http://example.test", DeletionFlag = true, DateCreated = DateTime.UtcNow };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        var timestamp = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 31; i++)
            db.AuditLogs.Add(new AuditLogEntity {
                ActorId = actor, HostId = host.Id, Actor = null!, Comment = $"change {i}",
                DateCreated = timestamp, Action = AuditAction.Update
            });
        db.AuditLogs.Add(new AuditLogEntity {
            ActorId = otherActor, UserId = actor, Actor = null!, Comment = "permission event",
            DateCreated = timestamp.AddDays(1), Action = AuditAction.PermissionsChanged
        });
        await db.SaveChangesAsync();
        await db.Users.Where(u => u.Id == actor).ExecuteUpdateAsync(u => u.SetProperty(x => x.DeletionFlag, true));
        var service = new AuditLogService(new AuditLogRepository(db));
        var query = new AuditLogQuery { Resource = "Hosts", Action = AuditAction.Update, ActorId = actor, TargetId = host.Id,
            FromUtc = timestamp.Date, UntilUtc = timestamp.Date.AddDays(1) };
        var first = await service.SearchAsync(query);
        var second = await service.SearchAsync(query with { Page = 2 });
        Assert.Equal(31, first.TotalItems);
        Assert.Equal(25, first.Items.Count);
        Assert.Equal(6, second.Items.Count);
        Assert.Empty(first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)));
        Assert.True(first.Items.Last().Id > second.Items.First().Id);
        Assert.All(first.Items, log => {
            Assert.Equal("retired-host", log.TargetName);
            Assert.Equal("audit-actor", log.ActorName);
            Assert.Equal(AuditAction.Update, log.Action);
        });
        Assert.Equal(1, (await service.SearchAsync(query with { Page = -10 })).Page);
        Assert.Equal(2, (await service.SearchAsync(query with { Page = int.MaxValue })).Page);
        Assert.Empty((await service.SearchAsync(query with { Action = AuditAction.Delete })).Items);
        Assert.Single((await service.SearchAsync(new AuditLogQuery { Resource = "Users", ActorId = otherActor, Action = AuditAction.PermissionsChanged })).Items);
        Assert.Contains(await service.GetActorsAsync(), item => item.Id == actor);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SearchAsync(query with { Resource = "Invalid" }));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SearchAsync(query with { Resource = null }));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SearchAsync(query with { Action = (AuditAction)99 }));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SearchAsync(query with { UntilUtc = timestamp.Date.AddDays(-1) }));
    }

    [PostgresFact]
    public async Task ExistingAuditRowsMigrateAsLegacy() {
        var actor = await Register("legacy-actor");
        await using var db = Context();
        var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        await db.GetService<IMigrator>().MigrateAsync("20260826164258_AddMinecraftModules");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO "AuditLogs" ("ActorId", "UserId", "Comment", "DateCreated") VALUES ({actor}, {actor}, {"Created module."}, {DateTime.UtcNow})""");
        await db.Database.MigrateAsync();
        var result = await new AuditLogService(new AuditLogRepository(db)).SearchAsync(new AuditLogQuery { Action = AuditAction.Legacy });
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(AuditAction.Legacy, item.Action));
    }

    [PostgresFact]
    public async Task MutationsWriteExplicitActionsRegardlessOfComment() {
        var actor = await Register("writer");
        var target = await Register("target");
        await using var db = Context();
        var hosts = new HostRepository(db);
        var host = new HostModel { Name = "test-host", AgentBaseUrl = "http://example.test", IsEnabled = true, DateCreated = DateTime.UtcNow };
        await hosts.AddAsync(host, actor, "arbitrary comment");
        await hosts.UpdateAsync(host, actor, "Created module.");
        await hosts.DeleteAsync(host.Id, actor, "arbitrary comment");
        var users = new UserCrudAccessRepository(db);
        users.AddAuditLog(actor, target, "arbitrary comment");
        await users.SaveChangesAsync();
        await users.DeleteUserAsync(target, actor, "arbitrary comment");
        var actions = await db.AuditLogs.OrderBy(log => log.Id).Select(log => log.Action).ToListAsync();
        Assert.Equal(new[] { AuditAction.Create, AuditAction.Create, AuditAction.Create, AuditAction.Update, AuditAction.Delete, AuditAction.PermissionsChanged, AuditAction.Delete }, actions);
    }

    [PostgresFact]
    public async Task PermissionUpgradeGrantsHistoryWithoutControlPrivileges() {
        var user = await Register("existing-admin");
        await using var db = Context();
        await db.GetService<IMigrator>().MigrateAsync("20260929090847_AddAuditActionsAndPagingIndexes");
        await db.Database.MigrateAsync();
        var history = await db.UserCrudAccess.SingleAsync(x => x.UserId == user && x.Resource == "MonitoringHistory");
        Assert.True(history.CanRead);
        var module = await db.UserCrudAccess.SingleAsync(x => x.UserId == user && x.Resource == "Modules");
        Assert.False(module.CanOperate);
    }

    [PostgresFact]
    public async Task ConsolidatingOperationsResetsOldGrantsAndPreservesCommandAudit() {
        var actor = await Register("existing-operator");
        await using var db = Context();
        await db.GetService<IMigrator>().MigrateAsync("20260929103918_AddMonitoringHistory");
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE "UserCrudAccess" SET "CanStart" = true, "CanStop" = true,
                "CanRestart" = true, "CanEnable" = true, "CanDisable" = true,
                "CanExecuteCommand" = true WHERE "Resource" = 'Modules'
            """);
        var operationId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AuditLogs" ("ActorId", "Comment", "DateCreated", "Action", "OperationId", "Operation", "Outcome")
            VALUES ({actor}, {"Historical command"}, {DateTime.UtcNow}, {(int)AuditAction.CommandExecution}, {operationId}, {"execute"}, {"Succeeded"})
            """);
        await db.Database.MigrateAsync();
        Assert.False((await db.UserCrudAccess.SingleAsync(x => x.UserId == actor && x.Resource == "Modules")).CanOperate);
        var audit = await db.AuditLogs.SingleAsync(x => x.OperationId == operationId);
        Assert.Equal(AuditAction.CommandExecution, audit.Action);
        Assert.Equal("Succeeded", audit.Outcome);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task OperationsGrantImpliesReadAndOnlyAppliesToModules() {
        var admin = await Register("admin");
        var user = await Register("operator");
        await using var db = Context();
        var service = new UserCrudAccessService(new UserCrudAccessRepository(db));
        await service.SaveAccessAsync(user, [
            new() { Resource = "Modules", CanOperate = true },
            new() { Resource = "Hosts", CanOperate = true },
            new() { Resource = "AuditLogs", CanOperate = true }
        ], admin, "Grant operations");
        var grants = await service.GetAccessAsync(user);
        var module = Assert.Single(grants, x => x.Resource == "Modules");
        Assert.True(module.CanOperate); Assert.True(module.CanRead); Assert.False(module.CanUpdate);
        Assert.All(grants.Where(x => x.Resource != "Modules"), x => Assert.False(x.CanOperate));
        var sessions = new JwtSessionService(new UserAuthenticationRepository(db), Options.Create(new JwtOptions { SigningKey = new string('x', 64) }));
        var token = new JwtSecurityTokenHandler().ReadJwtToken((await sessions.RefreshTokenAsync("operator"))!);
        Assert.Contains(token.Claims, x => x.Value == ModuleAccess.Operations);
        Assert.Contains(token.Claims, x => x.Value == ModuleAccess.Read);
        Assert.DoesNotContain(token.Claims, x => x.Value is "Modules:start" or "Modules:executecommand");
        db.ChangeTracker.Clear();
        await service.SaveAccessAsync(user, [], admin, "Revoke operations");
        token = new JwtSecurityTokenHandler().ReadJwtToken((await sessions.RefreshTokenAsync("operator"))!);
        Assert.DoesNotContain(token.Claims, x => x.Value == ModuleAccess.Operations);
    }

    [PostgresFact]
    public async Task FirstUserGetsAllAccessAndLaterUsersGetNone() {
        var first = await Register("first");
        var second = await Register("second");
        await using var db = Context();
        var grants = await db.UserCrudAccess.Where(x => x.UserId == first).ToListAsync();
        Assert.Equal(6, grants.Count);
        Assert.All(grants, grant => {
            Assert.True(grant.CanRead);
            Assert.False(grant.CanOperate);
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
        Assert.Equal(6, await db.UserCrudAccess.CountAsync());
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
            new() { Resource = "AuditLogs", CanRead = true, CanDelete = true },
            new() { Resource = "MonitoringHistory", CanRead = true, CanUpdate = true }
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
