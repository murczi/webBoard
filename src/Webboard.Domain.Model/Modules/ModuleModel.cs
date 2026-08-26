namespace Webboard.Domain.Model.Modules;

public class ModuleModel {
    public int Id { get; set; }
    public int TypeId { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public int? HostId { get; set; }
    public string? HostName { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? HealthCheckUrl { get; set; }
    public string? ManagementUrl { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset DateCreated { get; set; }
    public DateTimeOffset DateUpdated { get; set; }
}
