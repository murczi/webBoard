namespace Webboard.Domain.Model.Modules;
using Webboard.Contracts;

public sealed record ModuleOperationRequest(Guid RequestId, int ModuleId, string Operation);
public sealed record ModuleOperationResult(bool Success, string Message, int? ExitCode = null, string? Output = null, bool TimedOut = false)
    : AgentOperationResult(Success, Message, ExitCode, Output, TimedOut);
