namespace WebBoard.Agent;

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

public sealed record OperationRequest(Guid RequestId, int ModuleId, string Kind, string Target, string Operation);
public sealed record OperationResult(bool Success, string Message, int? ExitCode = null, string? Output = null, bool TimedOut = false);

public interface IProcessRunner {
    Task<OperationResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken);
}
public sealed class ProcessRunner : IProcessRunner {
    public async Task<OperationResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken) {
        if (OperatingSystem.IsLinux() && !File.Exists("/usr/bin/setsid")) return new(false, "Install util-linux (setsid) for bounded process execution.");
        var isolatedGroup = OperatingSystem.IsLinux();
        var start = new ProcessStartInfo(isolatedGroup ? "/usr/bin/setsid" : executable) {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        start.Environment.Clear();
        start.Environment["PATH"] = "/usr/sbin:/usr/bin:/sbin:/bin";
        start.Environment["LANG"] = "C.UTF-8";
        if (isolatedGroup) { start.ArgumentList.Add("--wait"); start.ArgumentList.Add("--"); start.ArgumentList.Add(executable); }
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try {
            if (!process.Start()) return new(false, "Process could not be started.");
            var output = ReadBoundedAsync(process.StandardOutput, deadline.Token);
            var error = ReadBoundedAsync(process.StandardError, deadline.Token);
            try { await Task.WhenAll(process.WaitForExitAsync(deadline.Token), output, error); }
            catch (OperationCanceledException) {
                if (isolatedGroup) KillProcessGroup(-process.Id, 9);
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                try { await Task.WhenAll(output, error); } catch (OperationCanceledException) { }
                return new(false, cancellationToken.IsCancellationRequested ? "Execution interrupted." : "Execution timed out.", TimedOut: true);
            }
            return new(process.ExitCode == 0, process.ExitCode == 0 ? "Operation completed." : "Operation failed.",
                process.ExitCode, (await output) + (await error));
        }
        catch (System.ComponentModel.Win32Exception) { return new(false, "Executable could not be started. Check installation and permissions."); }
        catch (IOException) { return new(false, "Could not capture process output."); }
    }
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int KillProcessGroup(int processId, int signal);
    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token) {
        var text = new StringBuilder();
        var buffer = new char[2048];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
            if (text.Length < 8192) text.Append(buffer, 0, Math.Min(count, 8192 - text.Length));
        return text.ToString();
    }
}

public sealed class AgentOperations(IConfiguration configuration, DockerSocketClient docker, IProcessRunner process) {
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
                    !Regex.IsMatch(request.Target, @"\A[A-Za-z0-9_][A-Za-z0-9_.@:\\-]{0,249}\.service\z"))
                    throw new ArgumentException("Invalid service operation.");
                if (!Allows("Systemd", request.Target)) throw new UnauthorizedAccessException();
                result = await new SystemdClient(process).ControlAsync(request.Target, request.Operation, cancellationToken);
            }
            else throw new ArgumentException("Unknown operation kind.");
            if (completed.Count >= 2048) completed.Remove(completed.Keys.First());
            completed[request.RequestId] = (request, result);
            return result;
        }
        finally { gate.Release(); }
    }
}
