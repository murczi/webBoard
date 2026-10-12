namespace Webboard.Domain.Model.Modules;

public sealed record ModuleTypeDefinition(int Id, string Name, string Description, bool Host,
    bool Health = false, string? Remote = null, string? Server = null, bool SupportsOperations = false);

public static class ModuleTypes {
    public const string Http = "Http", Docker = "Docker", Systemd = "Systemd", Minecraft = "Minecraft", Steam = "Steam";
    public static IReadOnlyList<ModuleTypeDefinition> All { get; } = Array.AsReadOnly(new[] {
        new ModuleTypeDefinition(1, Http, "Http Module", Host: true, Health: true),
        new ModuleTypeDefinition(2, Docker, "Docker container", Host: true, Remote: "container", SupportsOperations: true),
        new ModuleTypeDefinition(3, Systemd, "systemd service", Host: true, Remote: "service", SupportsOperations: true),
        new ModuleTypeDefinition(4, Minecraft, "Minecraft Java Edition server", Host: false, Server: "minecraft"),
        new ModuleTypeDefinition(5, Steam, "Steam A2S game server", Host: false, Server: "steam")
    });
    private static readonly IReadOnlyDictionary<string, ModuleTypeDefinition> byName = All.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
    public static ModuleTypeDefinition? Find(string? name) => name is not null && byName.TryGetValue(name, out var type) ? type : null;
    public static string CanonicalName(string name) => Find(name)?.Name ?? throw new ArgumentException("Unsupported module type.");
    public static bool Is(string name, string expected) => string.Equals(name, expected, StringComparison.OrdinalIgnoreCase);
}
