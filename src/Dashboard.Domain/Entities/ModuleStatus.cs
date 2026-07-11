using Dashboard.Domain.Modules;

namespace Dashboard.Domain.Entities;

public sealed class ModuleStatus
{
    public Guid DashboardModuleId { get; set; }

    public ModuleHealth Health { get; set; } = ModuleHealth.Unknown;

    public DateTimeOffset? CheckedAtUtc { get; set; }

    public int? ResponseTimeMilliseconds { get; set; }

    public string? Message { get; set; }

    public DashboardModule Module { get; set; } = null!;
}