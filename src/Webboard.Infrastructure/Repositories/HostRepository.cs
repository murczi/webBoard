namespace Webboard.Infrastructure.Repositories;

using Configuration;
using Configuration.Entities;
using Domain.Interfaces.Repositories;
using Domain.Model.Hosts;
using Microsoft.EntityFrameworkCore;

public class HostRepository(WebboardDbContext dbContext) : IHostRepository {
    public async Task<IReadOnlyList<HostModel>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Hosts.AsNoTracking()
            .Where(host => !host.DeletionFlag)
            .OrderBy(host => host.Name)
            .Select(host => new HostModel
            {
                Id = host.Id,
                Name = host.Name,
                AgentBaseUrl = host.AgentBaseUrl,
                IsEnabled = host.IsEnabled,
                DateCreated = host.DateCreated
            })
            .ToListAsync(cancellationToken);

    public Task<bool> NameExistsAsync(
        string name,
        int? excludingHostId = null,
        CancellationToken cancellationToken = default) {
        var normalizedName = name.ToLower();
        return dbContext.Hosts.AnyAsync(
            host => host.Name.ToLower() == normalizedName &&
                    (!excludingHostId.HasValue || host.Id != excludingHostId.Value),
            cancellationToken);
    }

    public async Task AddAsync(HostModel host, CancellationToken cancellationToken = default) {
        var entity = new HostEntity
        {
            Name = host.Name,
            AgentBaseUrl = host.AgentBaseUrl,
            IsEnabled = host.IsEnabled,
            DeletionFlag = false,
            DateCreated = host.DateCreated
        };
        dbContext.Hosts.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        host.Id = entity.Id;
    }

    public async Task<bool> UpdateAsync(HostModel host, CancellationToken cancellationToken = default) =>
        await dbContext.Hosts
            .Where(entity => entity.Id == host.Id && !entity.DeletionFlag)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(entity => entity.Name, host.Name)
                    .SetProperty(entity => entity.AgentBaseUrl, host.AgentBaseUrl)
                    .SetProperty(entity => entity.IsEnabled, host.IsEnabled),
                cancellationToken) > 0;

    public async Task<bool> DeleteAsync(int hostId, CancellationToken cancellationToken = default) =>
        await dbContext.Hosts
            .Where(host => host.Id == hostId && !host.DeletionFlag)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(host => host.DeletionFlag, true)
                    .SetProperty(host => host.IsEnabled, false),
                cancellationToken) > 0;
}
