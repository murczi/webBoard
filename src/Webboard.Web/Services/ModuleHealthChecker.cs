namespace Webboard.Web.Services;
using Domain.Interfaces.Services;
using Domain.Model.Modules;

public sealed class ModuleHealthChecker : IModuleHealthChecker {
    private readonly IReadOnlyDictionary<string, IModuleHealthHandler> handlers;
    public ModuleHealthChecker(IEnumerable<IModuleHealthHandler> handlers) => this.handlers = handlers.ToDictionary(x => x.TypeName, StringComparer.OrdinalIgnoreCase);
    public ModuleHealthChecker(HttpClient client, IDockerAgentClient docker, ISystemdAgentClient systemd, IMinecraftStatusClient minecraft)
        : this([new HttpHealthHandler(client), new DockerHealthHandler(docker), new SystemdHealthHandler(systemd), new MinecraftHealthHandler(minecraft)]) { }
    public Task<ModuleHealthResult> CheckAsync(ModuleModel module, CancellationToken cancellationToken = default) =>
        handlers.TryGetValue(module.TypeName, out var handler) ? handler.CheckAsync(module, cancellationToken)
        : Task.FromResult(new ModuleHealthResult(ModuleHealthState.NotConfigured, null, "Unsupported module type"));
}
