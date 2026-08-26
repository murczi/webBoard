namespace Webboard.Domain.Interfaces.Repositories;

using Model.Hosts;

public interface IHostRepository {
    Task<IReadOnlyList<HostModel>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(string name, int? excludingHostId = null, CancellationToken cancellationToken = default);
    Task AddAsync(HostModel host, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(HostModel host, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int hostId, CancellationToken cancellationToken = default);
}
