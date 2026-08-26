namespace Webboard.Domain.Interfaces.Services;

using Model.Modules;

public interface IModuleHealthChecker {
    Task<ModuleHealthResult> CheckAsync(
        ModuleModel module,
        CancellationToken cancellationToken = default);
}
