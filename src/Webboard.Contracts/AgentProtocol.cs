namespace Webboard.Contracts;

public static class AgentProtocol {
    public const int Version = 1;
    public const string Header = "X-Webboard-Agent-Protocol";
    // Missing versions are the original wire format, which version 1 preserves.
    public static bool Supports(int? version) => version is null or Version;
    public static bool SupportsHeader(IEnumerable<string> values) {
        var versions = values.ToArray();
        return versions.Length == 0 || versions.Length == 1 && versions[0] == "1";
    }
}
public sealed record AgentHealthResponse(string? Status, string? Service = null, int? ProtocolVersion = null);
public record AgentOperationRequest(Guid RequestId, int ModuleId, string Kind, string Target, string Operation);
public record AgentOperationResult(bool Success, string Message, int? ExitCode = null, string? Output = null, bool TimedOut = false) {
    public OperationOutcome Outcome { get; init; } = Success ? OperationOutcome.Succeeded : TimedOut ? OperationOutcome.TimedOut : OperationOutcome.Failed;
    public OperationFailureReason FailureReason { get; init; } = TimedOut ? OperationFailureReason.Timeout : OperationFailureReason.None;
}
