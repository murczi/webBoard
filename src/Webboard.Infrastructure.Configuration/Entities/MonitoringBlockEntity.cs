namespace Webboard.Infrastructure.Configuration.Entities;
using Webboard.Domain.Model.Modules;

public sealed class MonitoringBlockEntity {
    public long Id { get; set; }
    public int ModuleId { get; set; }
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public ModuleHealthState State { get; set; }
    public string Message { get; set; } = "";
}
