namespace Webboard.Infrastructure.Configuration.Entities;

public class ModuleTypeEntity {
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string Description { get; set; }

    public ICollection<ModuleEntity> Modules { get; set; }
        = new List<ModuleEntity>();
}
