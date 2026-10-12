namespace Webboard.Domain.Model.Modules;

public static class ModuleEligibility {
    public static string? UnavailableReason(bool enabled, int? hostId, bool hostExists, bool hostEnabled, bool hostDeleted) =>
        !enabled ? "Monitoring paused: module is disabled" : !hostId.HasValue ? null :
        !hostExists || hostDeleted ? "Monitoring paused: host is unavailable" :
        !hostEnabled ? "Monitoring paused: host is disabled" : null;
    public static bool CanOperate(ModuleModel module) => module.HostId.HasValue && module.HostAgentBaseUrl is not null &&
        UnavailableReason(module.IsEnabled, module.HostId, module.HostExists, module.HostIsEnabled, module.HostIsDeleted) is null &&
        (module.TypeName == "Docker" && !string.IsNullOrWhiteSpace(module.ContainerId) ||
         module.TypeName == "Systemd" && !string.IsNullOrWhiteSpace(module.ServiceName));
}
