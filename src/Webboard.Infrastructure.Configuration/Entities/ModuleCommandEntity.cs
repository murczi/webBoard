namespace Webboard.Infrastructure.Configuration.Entities;

public sealed class ModuleCommandEntity {
    public int Id { get; set; }
    public int ModuleId { get; set; }
    public int HostId { get; set; }
    public required string CommandId { get; set; }
    public required string Label { get; set; }
    public ModuleEntity Module { get; set; } = null!;
    public HostEntity Host { get; set; } = null!;
}
