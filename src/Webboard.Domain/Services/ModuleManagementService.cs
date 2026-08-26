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

    public async Task<ModuleModel> AddAsync(
        ModuleModel module,
        int actorId,
        string auditComment,
        CancellationToken cancellationToken = default) {
        await ValidateAndNormalizeAsync(module, cancellationToken);
        auditComment = AuditComment.Normalize(auditComment);
        var now = DateTimeOffset.UtcNow;
        module.DateCreated = now;
        module.DateUpdated = now;
        await repository.AddAsync(module, actorId, auditComment, cancellationToken);
        return module;
    }

    public async Task<bool> UpdateAsync(
        ModuleModel module,
        int actorId,
        string auditComment,
        CancellationToken cancellationToken = default) {
        await ValidateAndNormalizeAsync(module, cancellationToken);
        auditComment = AuditComment.Normalize(auditComment);
        module.DateUpdated = DateTimeOffset.UtcNow;
        return await repository.UpdateAsync(module, actorId, auditComment, cancellationToken);
    }

    public Task<bool> DeleteAsync(
        int moduleId,
        int actorId,
        string auditComment,
        CancellationToken cancellationToken = default) =>
        repository.DeleteAsync(
            moduleId,
            actorId,
            AuditComment.Normalize(auditComment),
            cancellationToken);

    private async Task ValidateAndNormalizeAsync(
        ModuleModel module,
        CancellationToken cancellationToken) {
        module.Name = module.Name.Trim();
        module.Description = EmptyToNull(module.Description);
        module.ManagementUrl = NormalizeOptionalUrl(module.ManagementUrl, "Management URL");

        if (module.Name.Length is 0 or > 100)
            throw new ArgumentException("Module name must be between 1 and 100 characters.", nameof(module));
        if (module.Description?.Length > 1000)
            throw new ArgumentException("Description cannot exceed 1000 characters.", nameof(module));
        var typeName = await repository.GetTypeNameAsync(module.TypeId, cancellationToken);
        if (typeName is null)
            throw new ArgumentException("Select a valid module type.", nameof(module));
        if (module.HostId.HasValue &&
            !await repository.HostExistsAsync(module.HostId.Value, cancellationToken))
            throw new ArgumentException("Select a valid host or leave the host empty.", nameof(module));

        if (string.Equals(typeName, "Docker", StringComparison.OrdinalIgnoreCase)) {
            module.HealthCheckUrl = null;
            module.ServiceName = null;
            module.ContainerId = EmptyToNull(module.ContainerId);
            if (!module.HostId.HasValue)
                throw new ArgumentException("Select a host for a Docker module.", nameof(module));
            if (module.ContainerId is null)
                throw new ArgumentException("Select a Docker container.", nameof(module));
            if (module.ContainerId.Length > 128)
                throw new ArgumentException("Docker container ID cannot exceed 128 characters.", nameof(module));
        }
        else if (string.Equals(typeName, "Systemd", StringComparison.OrdinalIgnoreCase)) {
            module.HealthCheckUrl = null;
            module.ContainerId = null;
            module.ServiceName = EmptyToNull(module.ServiceName);
            if (!module.HostId.HasValue)
                throw new ArgumentException("Select a host for a systemd module.", nameof(module));
            if (module.ServiceName is null)
                throw new ArgumentException("Select a systemd service.", nameof(module));
            if (!IsValidSystemdServiceName(module.ServiceName))
                throw new ArgumentException("Select a valid systemd service.", nameof(module));
        }
        else {
            module.HealthCheckUrl = NormalizeOptionalUrl(module.HealthCheckUrl, "Health-check URL");
            module.ContainerId = null;
            module.ServiceName = null;
        }
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsValidSystemdServiceName(string value) =>
        value.Length <= 256 &&
        value.EndsWith(".service", StringComparison.OrdinalIgnoreCase) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) ||
            character is '_' or '.' or '@' or ':' or '\\' or '-');

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
