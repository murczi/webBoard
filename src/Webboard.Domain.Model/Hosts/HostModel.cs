namespace Webboard.Domain.Model.Hosts;

public class HostModel {
    public long Version { get; set; }
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string AgentBaseUrl { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset DateUpdated { get; set; }
    public DateTimeOffset DateCreated { get; set; }
}
