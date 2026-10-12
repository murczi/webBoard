namespace Webboard.Domain.Model.Modules;

public sealed record ModuleOperationRequest(Guid RequestId, int ModuleId, string Operation);
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<OperationOutcome>))]
public enum OperationOutcome { Succeeded, Rejected, Failed, TimedOut, Unknown, PartiallyCompleted }
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<OperationFailureReason>))]
public enum OperationFailureReason { None, Timeout, Transport, Authorization, Busy, Interrupted, InvalidResponse, PartialCompletion }
public sealed record ModuleOperationResult(bool Success, string Message, int? ExitCode = null, string? Output = null, bool TimedOut = false) {
    public OperationOutcome Outcome { get; init; } = Success ? OperationOutcome.Succeeded : TimedOut ? OperationOutcome.TimedOut : OperationOutcome.Failed;
    public OperationFailureReason FailureReason { get; init; } = TimedOut ? OperationFailureReason.Timeout : OperationFailureReason.None;
}
