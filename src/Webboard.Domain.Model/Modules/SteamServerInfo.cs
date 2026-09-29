namespace Webboard.Domain.Model.Modules;
public sealed record SteamServerInfo(string Name, string Map, string Game, int Players, int MaxPlayers, string Version,
    IReadOnlyList<SteamPlayer>? PlayerDetails = null);
public sealed record SteamPlayer(string Name, int Score, float DurationSeconds);
