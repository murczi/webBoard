namespace Webboard.Domain.Interfaces.Services;
using Model.Modules;
public interface ISteamQueryClient {
    Task<ModuleHealthResult> CheckAsync(string address, int port, bool queryPlayers, CancellationToken cancellationToken);
}
public interface IModuleHealthHandler {
    string TypeName { get; }
    Task<ModuleHealthResult> CheckAsync(ModuleModel module, CancellationToken cancellationToken);
}
