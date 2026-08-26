namespace Webboard.Domain.Interfaces.Services;

using Model.Modules;

public interface IMinecraftStatusClient {
    Task<ModuleHealthResult> CheckServerAsync(
        string serverAddress,
        int serverPort,
        CancellationToken cancellationToken = default);
}
