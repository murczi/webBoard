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
