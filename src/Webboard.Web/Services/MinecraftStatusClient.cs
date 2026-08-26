namespace Webboard.Web.Services;

using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Domain.Interfaces.Services;
using Domain.Model.Modules;

public sealed class MinecraftStatusClient : IMinecraftStatusClient {
    private const int MaximumPacketLength = 1024 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    public async Task<ModuleHealthResult> CheckServerAsync(
        string serverAddress,
        int serverPort,
        CancellationToken cancellationToken = default) {
        if (!IsValidEndpoint(serverAddress, serverPort))
            return new ModuleHealthResult(ModuleHealthState.Unhealthy, null, "Invalid Minecraft server address or port");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try {
            return await GetStatusAsync(serverAddress, serverPort, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return new ModuleHealthResult(ModuleHealthState.Unhealthy, null, "Minecraft check timed out");
        }
        catch (Exception exception) when (exception is SocketException or IOException or JsonException) {
            return new ModuleHealthResult(ModuleHealthState.Unhealthy, null, "Minecraft server is unavailable");
        }
    }

    private static async Task<ModuleHealthResult> GetStatusAsync(
        string address,
        int port,
        CancellationToken cancellationToken) {
        using var client = new TcpClient();
        var stopwatch = Stopwatch.StartNew();
        await client.ConnectAsync(address, port, cancellationToken);
        await using var stream = client.GetStream();

        await WriteHandshakeAsync(stream, address, port, cancellationToken);
        await stream.WriteAsync(new byte[] { 1, 0 }, cancellationToken);

        var packetLength = await ReadVarIntAsync(stream, cancellationToken);
        if (packetLength is <= 0 or > MaximumPacketLength)
            throw new InvalidDataException("Invalid Minecraft status packet length.");

        if (await ReadVarIntAsync(stream, cancellationToken) != 0)
            throw new InvalidDataException("Unexpected Minecraft status packet.");

        var jsonLength = await ReadVarIntAsync(stream, cancellationToken);
        if (jsonLength < 0 || jsonLength > packetLength || jsonLength > MaximumPacketLength)
            throw new InvalidDataException("Invalid Minecraft status response length.");

        var jsonBytes = new byte[jsonLength];
        await stream.ReadExactlyAsync(jsonBytes, cancellationToken);
        stopwatch.Stop();

        using var document = JsonDocument.Parse(jsonBytes);
        var root = document.RootElement;
        var version = GetString(root, "version", "name");
        var onlinePlayers = GetInt32(root, "players", "online");
        var maximumPlayers = GetInt32(root, "players", "max");
        var players = onlinePlayers.HasValue && maximumPlayers.HasValue
            ? $"{onlinePlayers}/{maximumPlayers} players"
            : "Online";
        var message = string.IsNullOrWhiteSpace(version) ? players : $"{players} · {version}";

        return new ModuleHealthResult(ModuleHealthState.Healthy, stopwatch.ElapsedMilliseconds, message);
    }

    private static bool IsValidEndpoint(string address, int port) =>
        !string.IsNullOrWhiteSpace(address) &&
        address.Length <= 253 &&
        !address.Any(char.IsWhiteSpace) &&
        !address.Contains('/') &&
        !address.Contains('\\') &&
        port is >= 1 and <= 65535;

    private static async Task WriteHandshakeAsync(
        Stream stream,
        string address,
        int port,
        CancellationToken cancellationToken) {
        var addressBytes = Encoding.UTF8.GetBytes(address);
        using var payload = new MemoryStream();
        WriteVarInt(payload, 0);
        WriteVarInt(payload, -1);
        WriteVarInt(payload, addressBytes.Length);
        payload.Write(addressBytes);
        Span<byte> portBytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(portBytes, (ushort)port);
        payload.Write(portBytes);
        WriteVarInt(payload, 1);

        using var packet = new MemoryStream();
        WriteVarInt(packet, checked((int)payload.Length));
        payload.Position = 0;
        payload.CopyTo(packet);
        await stream.WriteAsync(packet.ToArray(), cancellationToken);
    }

    private static void WriteVarInt(Stream stream, int value) {
        var remaining = unchecked((uint)value);
        do {
            var current = (byte)(remaining & 0x7F);
            remaining >>= 7;
            if (remaining != 0)
                current |= 0x80;
            stream.WriteByte(current);
        } while (remaining != 0);
    }

    private static async Task<int> ReadVarIntAsync(Stream stream, CancellationToken cancellationToken) {
        var value = 0;
        for (var position = 0; position < 35; position += 7) {
            var buffer = new byte[1];
            await stream.ReadExactlyAsync(buffer, cancellationToken);
            value |= (buffer[0] & 0x7F) << position;
            if ((buffer[0] & 0x80) == 0)
                return value;
        }

        throw new InvalidDataException("Invalid Minecraft VarInt.");
    }

    private static string? GetString(JsonElement root, string parent, string property) =>
        root.TryGetProperty(parent, out var parentElement) &&
        parentElement.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt32(JsonElement root, string parent, string property) =>
        root.TryGetProperty(parent, out var parentElement) &&
        parentElement.TryGetProperty(property, out var value) &&
        value.TryGetInt32(out var result)
            ? result
            : null;
}
