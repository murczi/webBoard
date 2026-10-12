namespace Webboard.Agent;

using Webboard.Contracts;

public sealed partial class SystemdClient {
    private readonly IProcessRunner process;
    public SystemdClient() : this(new ProcessRunner()) { }
    public SystemdClient(IProcessRunner process) => this.process = process;
    public async Task<OperationResult> ControlAsync(string serviceName, string operation, CancellationToken cancellationToken) {
        if (!IsValidServiceName(serviceName) || serviceName.StartsWith('-') || operation is not ("start" or "stop" or "restart" or "enable" or "disable"))
            throw new ArgumentException("Invalid systemd operation.");
        if (operation is "enable" or "disable") {
            // Call the host manager directly: systemctl's unit-file commands can operate
            // on the container filesystem instead of the mounted host system bus.
            string[] arguments = operation == "enable"
                ? ["asbb", "1", serviceName, "false", "false"]
                : ["asb", "1", serviceName, "false"];
            var result = await CallManagerAsync(operation == "enable" ? "EnableUnitFiles" : "DisableUnitFiles", arguments, cancellationToken);
            if (!result.Success) return result with { Output = null };
            var reload = await CallManagerAsync("Reload", [], cancellationToken);
            if (!reload.Success) return reload with {
                Output = null,
                Outcome = OperationOutcome.PartiallyCompleted,
                FailureReason = OperationFailureReason.PartialCompletion,
                Message = "Unit-file change completed, but reloading the host systemd manager failed. Verify the service and host reload permission before retrying."
            };
            return result with { Output = null };
        }
        var control = await process.RunAsync("/usr/bin/systemctl",
        ["--no-ask-password", operation, "--", serviceName], TimeSpan.FromSeconds(30), cancellationToken);
        return control with { Output = null };
    }

    private Task<OperationResult> CallManagerAsync(string method, string[] arguments, CancellationToken token) =>
        process.RunAsync("/usr/bin/busctl",
        new[] { "--system", "--allow-interactive-authorization=no", "--timeout=30s", "call",
                "org.freedesktop.systemd1", "/org/freedesktop/systemd1", "org.freedesktop.systemd1.Manager", method }
            .Concat(arguments).ToArray(), TimeSpan.FromSeconds(30), token);

    public async Task<IReadOnlyList<SystemdServiceDto>> GetServicesAsync(
        CancellationToken cancellationToken) {
        var output = await RunSystemctlAsync([
            "show", "--type=service", "--all", "--no-pager",
            "--property=Id,Description,LoadState,ActiveState,SubState"
        ], cancellationToken);

        return ParsePropertyBlocks(output)
               .Where(properties =>
                   properties.TryGetValue("Id", out var id) && IsValidServiceName(id) &&
                   properties.GetValueOrDefault("LoadState") != "not-found")
               .Select(properties => new SystemdServiceDto(
               properties["Id"],
               properties.GetValueOrDefault("Description") ?? properties["Id"],
               properties.GetValueOrDefault("LoadState") ?? "unknown",
               properties.GetValueOrDefault("ActiveState") ?? "unknown",
               properties.GetValueOrDefault("SubState") ?? "unknown"))
               .OrderBy(service => service.Name, StringComparer.OrdinalIgnoreCase)
               .ToList();
    }

    public async Task<SystemdServiceStatusDto?> GetServiceStatusAsync(
        string serviceName,
        CancellationToken cancellationToken) {
        if (!IsValidServiceName(serviceName))
            return null;

        var output = await RunSystemctlAsync([
            "show", "--no-pager",
            "--property=Id,Description,LoadState,ActiveState,SubState", "--", serviceName
        ], cancellationToken, allowNonZeroExitCode: true);
        var properties = ParsePropertyBlocks(output).FirstOrDefault();
        if (properties is null ||
            properties.GetValueOrDefault("LoadState") is null or "not-found")
            return null;

        var activeState = properties.GetValueOrDefault("ActiveState") ?? "unknown";
        var subState = properties.GetValueOrDefault("SubState") ?? "unknown";
        return new SystemdServiceStatusDto(
        properties.GetValueOrDefault("Id") ?? serviceName,
        properties.GetValueOrDefault("Description") ?? serviceName,
        activeState,
        subState,
        string.Equals(activeState, "active", StringComparison.OrdinalIgnoreCase),
        $"{activeState} ({subState})");
    }

    private async Task<string> RunSystemctlAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken, bool allowNonZeroExitCode = false) {
        var result = await process.CaptureAsync("/usr/bin/systemctl", arguments, TimeSpan.FromSeconds(5), 1048576, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.TimedOut) throw new OperationCanceledException("The systemd request timed out.");
        if (!result.Started || result.Truncated) throw new InvalidOperationException(result.Truncated ? "systemd response exceeded the allowed size." : result.StandardError);
        if (result.ExitCode != 0 && !allowNonZeroExitCode) throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError) ? "systemctl failed." : result.StandardError.Trim());
        return result.StandardOutput;
    }

    private static IReadOnlyList<Dictionary<string, string>> ParsePropertyBlocks(string output) =>
        output.Replace("\r\n", "\n", StringComparison.Ordinal)
              .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
              .Select(block => block.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(line => line.Split('=', 2))
                                    .Where(parts => parts.Length == 2)
                                    .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal))
              .ToList();

    private static bool IsValidServiceName(string serviceName) => SystemdUnitName.IsValid(serviceName);

}
