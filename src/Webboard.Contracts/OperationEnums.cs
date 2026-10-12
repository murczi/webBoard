namespace Webboard.Contracts;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<OperationOutcome>))]
public enum OperationOutcome { Succeeded = 0, Rejected = 1, Failed = 2, TimedOut = 3, Unknown = 4, PartiallyCompleted = 5 }

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<OperationFailureReason>))]
public enum OperationFailureReason { None = 0, Timeout = 1, Transport = 2, Authorization = 3, Busy = 4, Interrupted = 5, InvalidResponse = 6, PartialCompletion = 7 }
