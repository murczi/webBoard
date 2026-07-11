using Dashboard.Domain.Modules;

namespace Dashboard.Domain.Overview.Models;

public sealed class ModuleTileModel
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public ModuleType Type { get; set; }

    public ModuleHealth Health { get; set; }

    public bool IsEnabled { get; set; }

    public string? ManagementUrl { get; set; }

    public Guid? ManagedHostId { get; set; }

    public string? ManagedHostName { get; set; }

    public int SortOrder { get; set; }
}
