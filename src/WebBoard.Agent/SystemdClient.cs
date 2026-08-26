namespace WebBoard.Agent;

using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

public sealed partial class SystemdClient {
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
            "show", serviceName, "--no-pager",
            "--property=Id,Description,LoadState,ActiveState,SubState"
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

    private static async Task<string> RunSystemctlAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool allowNonZeroExitCode = false) {
        var startInfo = new ProcessStartInfo
        {
            FileName = "systemctl",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try {
            process.Start();
        }
        catch (Win32Exception exception) {
            throw new InvalidOperationException("systemctl is not installed on this host.", exception);
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        try {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }

        var output = await standardOutput;
        var error = await standardError;
        if (process.ExitCode != 0 && !allowNonZeroExitCode)
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error) ? "systemctl failed." : error.Trim());
        return output;
    }

    private static IReadOnlyList<Dictionary<string, string>> ParsePropertyBlocks(string output) =>
        output.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(block => block.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal))
            .ToList();

    private static bool IsValidServiceName(string serviceName) =>
        serviceName.Length <= 256 && ServiceNamePattern().IsMatch(serviceName);

    [GeneratedRegex(@"^[A-Za-z0-9_.@:\\-]+\.service$", RegexOptions.CultureInvariant)]
    private static partial Regex ServiceNamePattern();
}

public sealed record SystemdServiceDto(
    string Name,
    string Description,
    string LoadState,
    string ActiveState,
    string SubState);

public sealed record SystemdServiceStatusDto(
    string Name,
    string Description,
    string ActiveState,
    string SubState,
    bool IsHealthy,
    string Message);
