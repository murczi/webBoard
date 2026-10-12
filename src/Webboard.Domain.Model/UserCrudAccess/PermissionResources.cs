namespace Webboard.Domain.Model.UserCrudAccess;

public sealed record PermissionResource(string Key, string Label, bool IsReadOnly = false, bool SupportsOperations = false);

public static class PermissionResources {
    public static IReadOnlyList<PermissionResource> All { get; } = Array.AsReadOnly<PermissionResource>([
        new("Users", "Users"), new("Hosts", "Hosts"), new("Modules", "Modules", SupportsOperations: true),
        new("ModuleTypes", "Module types", true), new("AuditLogs", "Audit logs", true), new("MonitoringHistory", "Monitoring history", true)
    ]);
    public static PermissionResource? Find(string key) => All.FirstOrDefault(x => x.Key == key);
    public static void Normalize(UserCrudAccessModel grant) {
        var resource = Find(grant.Resource) ?? throw new ArgumentException("Unknown permission resource.");
        grant.IsReadOnlyResource = resource.IsReadOnly;
        if (resource.IsReadOnly) grant.CanCreate = grant.CanUpdate = grant.CanDelete = false;
        if (!resource.SupportsOperations) grant.CanOperate = false;
        grant.CanRead |= grant.CanCreate || grant.CanUpdate || grant.CanDelete || grant.CanOperate;
    }
    public static IEnumerable<string> ActionsFor(UserCrudAccessModel grant) {
        var resource = Find(grant.Resource);
        if (resource is null) yield break;
        var create = !resource.IsReadOnly && grant.CanCreate;
        var update = !resource.IsReadOnly && grant.CanUpdate;
        var delete = !resource.IsReadOnly && grant.CanDelete;
        var operate = resource.SupportsOperations && grant.CanOperate;
        if (create) yield return "create"; if (update) yield return "update"; if (delete) yield return "delete";
        if (operate) yield return "operations";
        if (grant.CanRead || create || update || delete || operate) yield return "read";
    }
}
