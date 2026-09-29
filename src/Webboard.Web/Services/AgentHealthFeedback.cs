namespace Webboard.Web.Services;

using System.Net;
using Domain.Model.Modules;

internal static class AgentHealthFeedback {
    public static string ResponseFailure(HttpStatusCode status, string subsystem) => status switch {
        HttpStatusCode.ServiceUnavailable => $"Agent is reachable, but {subsystem} is unavailable",
        HttpStatusCode.GatewayTimeout => $"Agent is reachable, but the {subsystem} request timed out",
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"Agent denied access to {subsystem} (HTTP {(int)status})",
        _ => $"Agent returned HTTP {(int)status} while checking {subsystem}"
    };

    public static ModuleHealthResult WithHost(ModuleHealthResult result, string? hostName) {
        if (string.IsNullOrWhiteSpace(hostName)) return result;
        return result.Message switch {
            "Cannot reach the host agent" => result with { Message = $"Cannot reach the agent on {hostName}" },
            "Host agent request timed out" => result with { Message = $"Agent on {hostName} did not respond in time" },
            _ => result
        };
    }
}
