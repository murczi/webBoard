using Dashboard.Domain.Modules;

namespace Dashboard.Application.Overview.DTOs;

public sealed record ModuleTileDto(
    Guid Id,
    string Name,
    ModuleType Type,
    ModuleHealth Health,
    bool IsEnabled,
    string? ManagementUrl,
    Guid? ManagedHostId,
    string? ManagedHostName,
    int SortOrder);