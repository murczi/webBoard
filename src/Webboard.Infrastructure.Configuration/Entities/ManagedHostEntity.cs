namespace Webboard.Infrastructure.Configuration.Entities;

public class ManagedHostEntity {
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string AgentBaseUrl { get; set; }

    public bool IsEnabled { get; set; }

    public DateTime DateCreated { get; set; }

    public ICollection<ModuleEntity> Modules { get; set; }
        = new List<ModuleEntity>();
}
