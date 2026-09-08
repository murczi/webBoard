namespace Webboard.Infrastructure.Repositories;

using Configuration;
using Configuration.Entities;
using Domain.Interfaces.Repositories;
using Domain.Model.Authentication;
using Domain.Model.UserCrudAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;

public class UserAuthenticationRepository(WebboardDbContext dbContext)
    : IUserAuthenticationRepository {
    public async Task<UserLoginModel?> FindByNameAsync(
        string name,
        CancellationToken cancellationToken = default) {
        var normalizedName = name.ToLower();
        var user = await dbContext.Users
            .AsNoTracking()
            .Include(candidate => candidate.CrudAccess)
            .SingleOrDefaultAsync(
                candidate => candidate.Name.ToLower() == normalizedName &&
                             !candidate.DeletionFlag,
                cancellationToken);

        if (user is null)
            return null;

        return new UserLoginModel
        {
            Id = user.Id,
            Name = user.Name,
            PasswordHash = user.PasswordHash,
            Access = user.CrudAccess.Select(access => new UserCrudAccessModel
            {
                Id = access.Id,
                UserId = access.UserId,
                Resource = access.Resource,
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
        var normalizedName = name.ToLower();
        return dbContext.Users.AnyAsync(
            candidate => candidate.Name.ToLower() == normalizedName,
            cancellationToken);
    }

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

        var now = DateTime.UtcNow;
        var entity = new UserEntity
        {
            Name = name,
            PasswordHash = passwordHash,
            DeletionFlag = false,
            DateCreated = now,
            DateUpdated = now
        };
        if (!await dbContext.Users.AnyAsync(cancellationToken)) {
            foreach (var resource in new[] { "Users", "Hosts", "Modules", "ModuleTypes", "AuditLogs" }) {
                var readOnly = resource is "ModuleTypes" or "AuditLogs";
                entity.CrudAccess.Add(new UserCrudAccessEntity
                {
                    Resource = resource,
                    CanRead = true,
                    CanCreate = !readOnly,
                    CanUpdate = !readOnly,
                    CanDelete = !readOnly,
                    User = entity
                });
            }
        }
        dbContext.Users.Add(entity);
        dbContext.AuditLogs.Add(new AuditLogEntity
        {
            Comment = "Created user.",
            DateCreated = now,
            Actor = entity,
            User = entity
        });

        try {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
                  { SqlState: PostgresErrorCodes.UniqueViolation }) {
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }
}
