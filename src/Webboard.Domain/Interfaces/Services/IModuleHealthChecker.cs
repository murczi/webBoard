namespace Webboard.Domain.Interfaces.Services;

using Model.Modules;

public interface IModuleHealthChecker {
    Task<ModuleHealthResult> CheckAsync(
        string? healthCheckUrl,
        CancellationToken cancellationToken = default);
}
