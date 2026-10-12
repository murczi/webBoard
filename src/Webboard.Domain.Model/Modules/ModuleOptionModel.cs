namespace Webboard.Domain.Model.Modules;

public sealed record ModuleOptionModel(int Id, string Name) { public bool IsAvailable { get; init; } = true; }
