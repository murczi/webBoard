namespace Webboard.Infrastructure.Repositories;

using Configuration;
using Configuration.Entities;
using Domain.Interfaces.Repositories;
using Domain.Model.UserCrudAccess;
using Microsoft.EntityFrameworkCore;

public class UserCrudAccessRepository(WebboardDbContext dbContext)
    : IUserCrudAccessRepository {
    public async Task<IReadOnlyList<UserSummaryModel>> GetUsersAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.Users
            .AsNoTracking()
            .Where(user => !user.DeletionFlag)
            .OrderBy(user => user.Name)
            .Select(user => new UserSummaryModel
            {
                Id = user.Id,
                Name = user.Name
            })
            .ToListAsync(cancellationToken);

    public Task<bool> UserExistsAsync(
        int userId,
        CancellationToken cancellationToken = default) =>
        dbContext.Users.AnyAsync(
            user => user.Id == userId && !user.DeletionFlag,
            cancellationToken);

    public async Task<bool> DeleteUserAsync(
        int userId,
        int actorId,
        string auditComment,
        CancellationToken cancellationToken = default) {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var deleted = await dbContext.Users
            .Where(user => user.Id == userId && !user.DeletionFlag)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.DeletionFlag, true)
                    .SetProperty(user => user.DateUpdated, DateTime.UtcNow),
                cancellationToken) > 0;
        if (!deleted)
            return false;

        AddAuditLog(actorId, userId, auditComment);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<UserCrudAccessModel>> GetByUserIdAsync(
        int userId,
        CancellationToken cancellationToken = default) =>
        await dbContext.UserCrudAccess
            .AsNoTracking()
            .Where(access => access.UserId == userId)
            .OrderBy(access => access.Resource)
            .Select(access => new UserCrudAccessModel
            {
                Id = access.Id,
                UserId = access.UserId,
                Resource = access.Resource,
                CanCreate = access.CanCreate,
                CanRead = access.CanRead,
                CanUpdate = access.CanUpdate,
                CanDelete = access.CanDelete
            })
            .ToListAsync(cancellationToken);

    public async Task AddAsync(
        UserCrudAccessModel access,
        CancellationToken cancellationToken = default) =>
        await dbContext.UserCrudAccess.AddAsync(ToEntity(access), cancellationToken);

    public void Update(UserCrudAccessModel access) =>
        dbContext.UserCrudAccess.Update(ToEntity(access));

    public void Delete(UserCrudAccessModel access) =>
        dbContext.UserCrudAccess.Remove(ToEntity(access));

    public void AddAuditLog(int actorId, int userId, string auditComment) =>
        dbContext.AuditLogs.Add(new AuditLogEntity
        {
            ActorId = actorId,
            Comment = auditComment,
            DateCreated = DateTime.UtcNow,
            UserId = userId,
            Actor = null!
        });

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        await dbContext.SaveChangesAsync(cancellationToken);

    private static UserCrudAccessEntity ToEntity(UserCrudAccessModel access) => new()
    {
        Id = access.Id,
        UserId = access.UserId,
        Resource = access.Resource,
        CanCreate = access.CanCreate,
        CanRead = access.CanRead,
        CanUpdate = access.CanUpdate,
        CanDelete = access.CanDelete,
        User = null!
    };
}
