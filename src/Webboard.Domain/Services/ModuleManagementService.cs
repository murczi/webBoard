namespace Webboard.Domain.Services;

using Interfaces.Repositories;
using Interfaces.Services;
using Model.Modules;

public class ModuleManagementService(IModuleRepository repository) : IModuleManagementService {
    public Task<IReadOnlyList<ModuleModel>> GetAllAsync(CancellationToken cancellationToken = default) =>
        repository.GetAllAsync(cancellationToken);

    public Task<IReadOnlyList<ModuleOptionModel>> GetHostsAsync(CancellationToken cancellationToken = default) =>
        repository.GetHostsAsync(cancellationToken);

    public Task<IReadOnlyList<ModuleOptionModel>> GetTypesAsync(CancellationToken cancellationToken = default) =>
        repository.GetTypesAsync(cancellationToken);

    public async Task<ModuleModel> AddAsync(ModuleModel module, CancellationToken cancellationToken = default) {
        await ValidateAndNormalizeAsync(module, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        module.DateCreated = now;
        module.DateUpdated = now;
        await repository.AddAsync(module, cancellationToken);
        return module;
    }

    public async Task<bool> UpdateAsync(ModuleModel module, CancellationToken cancellationToken = default) {
        await ValidateAndNormalizeAsync(module, cancellationToken);
        module.DateUpdated = DateTimeOffset.UtcNow;
        return await repository.UpdateAsync(module, cancellationToken);
    }

    public Task<bool> DeleteAsync(int moduleId, CancellationToken cancellationToken = default) =>
        repository.DeleteAsync(moduleId, cancellationToken);

    private async Task ValidateAndNormalizeAsync(
        ModuleModel module,
        CancellationToken cancellationToken) {
        module.Name = module.Name.Trim();
        module.Description = EmptyToNull(module.Description);
        module.HealthCheckUrl = NormalizeOptionalUrl(module.HealthCheckUrl, "Health-check URL");
        module.ManagementUrl = NormalizeOptionalUrl(module.ManagementUrl, "Management URL");

        if (module.Name.Length is 0 or > 100)
            throw new ArgumentException("Module name must be between 1 and 100 characters.", nameof(module));
        if (module.Description?.Length > 1000)
            throw new ArgumentException("Description cannot exceed 1000 characters.", nameof(module));
        if (!await repository.TypeExistsAsync(module.TypeId, cancellationToken))
            throw new ArgumentException("Select a valid module type.", nameof(module));
        if (module.HostId.HasValue &&
            !await repository.HostExistsAsync(module.HostId.Value, cancellationToken))
            throw new ArgumentException("Select a valid host or leave the host empty.", nameof(module));
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeOptionalUrl(string? value, string label) {
        var candidate = EmptyToNull(value);
        if (candidate is null)
            return null;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException($"{label} must be an absolute HTTP or HTTPS URL without credentials.");
        if (candidate.Length > 2048)
            throw new ArgumentException($"{label} cannot exceed 2048 characters.");
        return uri.AbsoluteUri;
    }
}
