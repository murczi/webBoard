namespace Webboard.Infrastructure.Repositories;

using Configuration;
using Configuration.Entities;
using Domain.Interfaces.Repositories;
using Domain.Model.Authentication;
using Domain.Model.UserCrudAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

public sealed class UserAuthenticationRepository(WebboardDbContext dbContext)
    : IUserAuthenticationRepository {
    private readonly TimeProvider timeProvider = TimeProvider.System;
    public UserAuthenticationRepository(WebboardDbContext dbContext, TimeProvider timeProvider) : this(dbContext) => this.timeProvider = timeProvider;

    public async Task<UserLoginModel?> FindByNameAsync(
        string name,
        CancellationToken cancellationToken = default) {
        var user = await dbContext.Users.FromSqlInterpolated($"""
            SELECT * FROM "Users" WHERE lower("Name") = lower({name}) AND NOT "DeletionFlag"
            """)
            .AsNoTracking()
            .Include(candidate => candidate.CrudAccess)
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
            return null;

        return new UserLoginModel {
            Id = user.Id,
            Name = user.Name,
            PasswordHash = user.PasswordHash,
            Access = user.CrudAccess.Select(access => new UserCrudAccessModel {
                Id = access.Id,
                UserId = access.UserId,
                Resource = access.Resource,
                CanOperate = access.CanOperate,
                CanCreate = access.CanCreate,
                CanRead = access.CanRead,
                CanUpdate = access.CanUpdate,
                CanDelete = access.CanDelete
            }).ToList()
        };
    }

    public Task<bool> UserNameExistsAsync(
        string name,
        CancellationToken cancellationToken = default) {
        return NameExistsAsync(name, cancellationToken);
    }
    private async Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) =>
        await dbContext.Database.SqlQuery<int>($"""
            SELECT COUNT(*)::integer AS "Value" FROM "Users" WHERE lower("Name") = lower({name})
            """).SingleAsync(cancellationToken) > 0;

    public async Task<bool> CreateAsync(
        string name,
        string passwordHash,
        CancellationToken cancellationToken = default) {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // Serialize registration so concurrent first signups cannot both become administrators.
        await dbContext.Database.ExecuteSqlRawAsync(
            "LOCK TABLE \"Users\" IN SHARE ROW EXCLUSIVE MODE", cancellationToken);
        if (await UserNameExistsAsync(name, cancellationToken))
            return false;

        var now = timeProvider.GetUtcNow();
        var entity = new UserEntity {
            Name = name,
            PasswordHash = passwordHash,
            DeletionFlag = false,
            DateCreated = now,
            DateUpdated = now
        };
        if (!await dbContext.Users.AnyAsync(user => !user.Name.StartsWith("__legacy_actor_"), cancellationToken)) {
            foreach (var resource in PermissionResources.All) {
                var readOnly = resource.IsReadOnly;
                entity.CrudAccess.Add(new UserCrudAccessEntity {
                    Resource = resource.Key,
                    CanRead = true,
                    CanOperate = false,
                    CanCreate = !readOnly,
                    CanUpdate = !readOnly,
                    CanDelete = !readOnly,
                    User = entity
                });
            }
        }
        dbContext.Users.Add(entity);
        dbContext.AuditLogs.Add(new AuditLogEntity {
            Action = Webboard.Domain.Model.AuditLogs.AuditAction.Create,
            Comment = "Created user.",
            DateCreated = now,
            Actor = entity,
            User = entity
        });

        try {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        } catch (DbUpdateException exception)
              when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) {
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }
}
