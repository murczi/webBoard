namespace Webboard.Infrastructure.Configuration.Entities;

public class ModuleTypeEntity {
    public int Id { get; set; }

    public required string Type { get; set; }

    public required string Description { get; set; }
}
