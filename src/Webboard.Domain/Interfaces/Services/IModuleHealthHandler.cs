namespace Webboard.Domain.Interfaces.Services;
using Model.Modules;

public interface IModuleHealthHandler {
    string TypeName { get; }
    Task<ModuleHealthResult> CheckAsync(ModuleModel module, CancellationToken cancellationToken);
}
