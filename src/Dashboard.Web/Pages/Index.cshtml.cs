using Dashboard.Application.Overview.DTOs;
using Dashboard.Application.Overview.Interfaces;
using Dashboard.Domain.Modules;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dashboard.Web.Pages;

public class IndexModel(
    IDashboardOverviewService overviewService)
    : PageModel
{
    [BindProperty(SupportsGet = true)] public string GroupBy { get; set; } = "host";

    public IReadOnlyList<ModuleTileGroup> Groups { get; private set; }
        = [];

    public async Task OnGetAsync(
        CancellationToken cancellationToken)
    {
        GroupBy = string.Equals(
            GroupBy,
            "type",
            StringComparison.OrdinalIgnoreCase)
            ? "type"
            : "host";

        var tiles = await overviewService.GetTilesAsync(
            cancellationToken);

        Groups = GroupBy == "type"
            ? GroupByType(tiles)
            : GroupByHost(tiles);
    }

    private static IReadOnlyList<ModuleTileGroup> GroupByHost(
        IReadOnlyList<ModuleTileDto> tiles)
    {
        return tiles
            .GroupBy(tile => tile.ManagedHostName ?? "Unassigned")
            .OrderBy(group => group.Key == "Unassigned")
            .ThenBy(group => group.Key)
            .Select(group => new ModuleTileGroup(
                group.Key,
                group
                    .OrderBy(tile => tile.SortOrder)
                    .ThenBy(tile => tile.Name)
                    .ToList()))
            .ToList();
    }

    private static IReadOnlyList<ModuleTileGroup> GroupByType(
        IReadOnlyList<ModuleTileDto> tiles)
    {
        return tiles
            .GroupBy(tile => tile.Type)
            .OrderBy(group => group.Key)
            .Select(group => new ModuleTileGroup(
                GetTypeDisplayName(group.Key),
                group
                    .OrderBy(tile => tile.SortOrder)
                    .ThenBy(tile => tile.Name)
                    .ToList()))
            .ToList();
    }

    private static string GetTypeDisplayName(ModuleType type)
    {
        return type switch
        {
            ModuleType.Http => "HTTP services",
            ModuleType.DockerContainer => "Docker containers",
            ModuleType.SystemdService => "systemd services",
            ModuleType.GameServer => "Game servers",
            _ => type.ToString()
        };
    }

    public sealed record ModuleTileGroup(
        string Name,
        IReadOnlyList<ModuleTileDto> Modules);
}