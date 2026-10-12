namespace WebBoard.Agent;

using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;
using Webboard.Contracts;

// Source-compatible adapters; the wire fields and defaults belong to Contracts.
public sealed record OperationRequest(Guid RequestId, int ModuleId, string Kind, string Target, string Operation)
    : AgentOperationRequest(RequestId, ModuleId, Kind, Target, Operation);
public sealed record OperationResult(bool Success, string Message, int? ExitCode = null, string? Output = null, bool TimedOut = false)
    : AgentOperationResult(Success, Message, ExitCode, Output, TimedOut);

public sealed record ProcessCaptureResult(int? ExitCode, string StandardOutput, string StandardError,
    bool TimedOut = false, bool Interrupted = false, bool Truncated = false, bool Started = true);
public interface IProcessRunner {
    Task<OperationResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken);
    async Task<ProcessCaptureResult> CaptureAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, int maximumCharacters, CancellationToken cancellationToken) {
        var result = await RunAsync(executable, arguments, timeout, cancellationToken);
        return new(result.ExitCode ?? (result.Success ? 0 : null), result.Output ?? "", result.Success ? "" : result.Message, result.TimedOut);
    }
}
public sealed class ProcessRunner : IProcessRunner {
    public async Task<OperationResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken) {
        var result = await CaptureAsync(executable, arguments, timeout, 8192, cancellationToken);
        if (result.TimedOut || result.Interrupted) return new(false, result.Interrupted ? "Execution interrupted." : "Execution timed out.", TimedOut: result.TimedOut) {
            Outcome = OperationOutcome.Unknown, FailureReason = result.Interrupted ? OperationFailureReason.Interrupted : OperationFailureReason.Timeout
        };
        return new(result.Started && result.ExitCode == 0, !result.Started ? result.StandardError : result.ExitCode == 0 ? "Operation completed." : "Operation failed.",
            result.ExitCode, result.StandardOutput + result.StandardError);
    }
    public async Task<ProcessCaptureResult> CaptureAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, int maximumCharacters, CancellationToken cancellationToken) {
        if (maximumCharacters is < 1 or > 1048576) throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        if (OperatingSystem.IsLinux() && !File.Exists("/usr/bin/setsid")) return new(null, "", "Install util-linux (setsid) for bounded process execution.", Started: false);
        var isolatedGroup = OperatingSystem.IsLinux();
        var start = new ProcessStartInfo(isolatedGroup ? "/usr/bin/setsid" : executable) {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        start.Environment.Clear(); start.Environment["PATH"] = "/usr/sbin:/usr/bin:/sbin:/bin"; start.Environment["LANG"] = "C.UTF-8";
        if (isolatedGroup) { start.ArgumentList.Add("--wait"); start.ArgumentList.Add("--"); start.ArgumentList.Add(executable); }
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(timeout);
        try {
            if (!process.Start()) return new(null, "", "Process could not be started.", Started: false);
            var output = ReadBoundedAsync(process.StandardOutput, maximumCharacters, deadline.Token);
            var error = ReadBoundedAsync(process.StandardError, maximumCharacters, deadline.Token);
            try { await Task.WhenAll(process.WaitForExitAsync(deadline.Token), output, error); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException) {
                deadline.Cancel(); Kill();
                try { await Task.WhenAll(output, error); } catch (Exception reading) when (reading is OperationCanceledException or IOException) { }
                if (exception is IOException) return new(null, "", "Could not capture process output.", Started: false);
                return new(null, "", "", TimedOut: !cancellationToken.IsCancellationRequested, Interrupted: cancellationToken.IsCancellationRequested);
            }
            var stdout = await output; var stderr = await error;
            return new(process.ExitCode, stdout.Text, stderr.Text, Truncated: stdout.Truncated || stderr.Truncated);
        }
        catch (System.ComponentModel.Win32Exception) { return new(null, "", "Executable could not be started. Check installation and permissions.", Started: false); }
        void Kill() {
            if (isolatedGroup) KillProcessGroup(-process.Id, 9);
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
    }
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)] private static extern int KillProcessGroup(int processId, int signal);
    private static async Task<(string Text, bool Truncated)> ReadBoundedAsync(StreamReader reader, int maximum, CancellationToken token) {
        var text = new StringBuilder(); var buffer = new char[2048]; var truncated = false; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0) {
            var remaining = maximum - text.Length; if (count > remaining) truncated = true;
            if (remaining > 0) text.Append(buffer, 0, Math.Min(count, remaining));
        }
        return (text.ToString(), truncated);
    }
}

public sealed class AgentOperations(IConfiguration configuration, DockerSocketClient docker, IProcessRunner process) {
    private readonly SystemdClient systemd = new(process);
    public AgentOperations(IConfiguration configuration, DockerSocketClient docker, IProcessRunner process, SystemdClient systemd)
        : this(configuration, docker, process) => this.systemd = systemd;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<Guid, (OperationRequest Request, OperationResult Result)> completed = [];
    public bool Allows(string kind, string target) =>
        (configuration.GetSection($"Controls:{kind}").Get<string[]>() ?? [])
        .Contains(target, StringComparer.Ordinal);

    public async Task<OperationResult> ExecuteAsync(OperationRequest request, CancellationToken cancellationToken) {
        if (request.RequestId == Guid.Empty || request.ModuleId <= 0) throw new ArgumentException("Invalid operation request.");
        if (!await gate.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("Another operation is running on this agent.");
        try {
            if (completed.TryGetValue(request.RequestId, out var previous)) {
                if (previous.Request != request) throw new ArgumentException("Request ID has already been used.");
                return previous.Result;
            }
            OperationResult result;
            if (request.Kind == "Docker") {
                if (request.Operation is not ("start" or "stop" or "restart")) throw new ArgumentException("Unsupported Docker operation.");
                var container = await docker.GetContainerStatusAsync(request.Target, cancellationToken);
                if (container is null) return new(false, "Container was not found.");
                if (!Allows("Docker", container.Id) && !Allows("Docker", container.Name)) throw new UnauthorizedAccessException();
                result = await docker.ControlAsync(container.Id, request.Operation, cancellationToken);
            }
            else if (request.Kind == "Systemd") {
                if (request.Operation is not ("start" or "stop" or "restart" or "enable" or "disable") ||
                    !SystemdUnitName.IsValid(request.Target))
                    throw new ArgumentException("Invalid service operation.");
                if (!Allows("Systemd", request.Target)) throw new UnauthorizedAccessException();
                result = await systemd.ControlAsync(request.Target, request.Operation, cancellationToken);
            }
            else throw new ArgumentException("Unknown operation kind.");
            if (completed.Count >= 2048) completed.Remove(completed.Keys.First());
            completed[request.RequestId] = (request, result);
            return result;
        }
        finally { gate.Release(); }
    }
}
