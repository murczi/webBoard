namespace Dashboard.Domain.Entities;

public sealed class ManagedHost
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Name { get; set; }

    public required string AgentBaseUrl { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<DashboardModule> Modules { get; set; }
        = new List<DashboardModule>();
}