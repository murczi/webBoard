namespace Webboard.Domain.Model.Hosts;

public sealed record AgentHealthResult(bool IsHealthy, string Message);
