namespace Webboard.Contracts;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<OperationOutcome>))]
public enum OperationOutcome { Succeeded, Rejected, Failed, TimedOut, Unknown, PartiallyCompleted }

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<OperationFailureReason>))]
public enum OperationFailureReason { None, Timeout, Transport, Authorization, Busy, Interrupted, InvalidResponse, PartialCompletion }
