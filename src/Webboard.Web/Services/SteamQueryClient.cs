namespace Webboard.Web.Services;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Domain.Interfaces.Services;
using Domain.Model.Modules;
using Security;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.Checksum;

public sealed class SteamQueryClient(NetworkPolicy network) : ISteamQueryClient {
    public async Task<ModuleHealthResult> CheckAsync(string address, int port, bool queryPlayers, CancellationToken cancellationToken) {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var stopwatch = Stopwatch.StartNew();
        try {
            var addresses = await network.ResolveAsync(address, timeout.Token);
            using var udp = new UdpClient(addresses[0].AddressFamily);
            udp.Connect(addresses[0], port);
            byte[] request = [255, 255, 255, 255, 0x54, .. Encoding.ASCII.GetBytes("Source Engine Query\0")];
            var info = ParseInfo(await QueryAsync(udp, request, timeout.Token));
            var responseTime = stopwatch.ElapsedMilliseconds;
            if (queryPlayers) {
                try {
                    using var optionalTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                    optionalTimeout.CancelAfter(TimeSpan.FromSeconds(1));
                    var players = await QueryAsync(udp, [255, 255, 255, 255, 0x55, 255, 255, 255, 255], optionalTimeout.Token);
                    info = info with { PlayerDetails = ParsePlayers(players) };
                }
                catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ICSharpCode.SharpZipLib.SharpZipBaseException) { }
            }
            return new(ModuleHealthState.Healthy, responseTime, $"{info.Name} · {info.Players}/{info.MaxPlayers} players · {info.Map}") { Steam = info };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(ModuleHealthState.Unhealthy, null, "Steam query timed out"); }
        catch (Exception exception) when (exception is IOException or SocketException or HttpRequestException or ICSharpCode.SharpZipLib.SharpZipBaseException) {
            return new(ModuleHealthState.Unhealthy, null, "Steam server is unavailable or returned an incompatible response");
        }
    }
    private static async Task<byte[]> QueryAsync(UdpClient udp, byte[] request, CancellationToken token) {
        var packet = request;
        for (var challenge = 0; challenge < 3; challenge++) {
            await udp.SendAsync(packet, token);
            var response = await ReceiveAsync(udp, token);
            if (response.Length == 0) throw new InvalidDataException("Empty response.");
            if (response[0] != 0x41) return response;
            if (response.Length != 5) throw new InvalidDataException("Invalid challenge.");
            packet = request[4] == 0x54 ? [.. request, .. response.AsSpan(1).ToArray()]
                : [.. request.AsSpan(0, 5).ToArray(), .. response.AsSpan(1).ToArray()];
        }
        throw new InvalidDataException("Too many challenge exchanges.");
    }
    private static async Task<byte[]> ReceiveAsync(UdpClient udp, CancellationToken token) {
        var assembler = new SteamPacketAssembler();
        for (var count = 0; count < 128; count++) {
            var packet = (await udp.ReceiveAsync(token)).Buffer;
            var result = assembler.Add(packet, token);
            if (result is not null) return result;
        }
        throw new InvalidDataException("Too many fragments.");
    }
    public static SteamServerInfo ParseInfo(byte[] payload) {
        var reader = new PacketReader(payload);
        if (reader.Byte() != 0x49) throw new InvalidDataException("Unsupported info response.");
        reader.Byte();
        var name = reader.Text(); var map = reader.Text(); reader.Text(); var game = reader.Text();
        var appId = reader.UShort(); var players = reader.Byte(); var maximum = reader.Byte();
        reader.Byte(); reader.Byte(); reader.Byte(); reader.Byte(); reader.Byte();
        if (appId == 2400) reader.Skip(3); // The Ship adds mode, witnesses and duration.
        var version = reader.Text();
        if (reader.Remaining > 0) {
            var flags = reader.Byte();
            if ((flags & 0x80) != 0) reader.UShort();
            if ((flags & 0x10) != 0) reader.Skip(8);
            if ((flags & 0x40) != 0) { reader.UShort(); reader.Text(); }
            if ((flags & 0x20) != 0) reader.Text();
            if ((flags & 0x01) != 0) reader.Skip(8);
        }
        if (reader.Remaining != 0) throw new InvalidDataException("Unexpected trailing info data.");
        return new(name, map, game, players, maximum, version);
    }
    public static IReadOnlyList<SteamPlayer> ParsePlayers(byte[] payload) {
        var reader = new PacketReader(payload);
        if (reader.Byte() != 0x44) throw new InvalidDataException("Unsupported player response.");
        var count = reader.Byte();
        var players = new List<SteamPlayer>();
        for (var index = 0; index < count; index++) {
            reader.Byte(); var name = reader.Text(); var score = reader.Int();
            var duration = BitConverter.Int32BitsToSingle(reader.Int());
            if (!float.IsFinite(duration) || duration < 0) throw new InvalidDataException("Invalid player duration.");
            players.Add(new(name, score, duration));
        }
        if (reader.Remaining != 0) throw new InvalidDataException("Unexpected player data.");
        return players;
    }
    private sealed class PacketReader(byte[] bytes) {
        private int offset;
        public int Remaining => bytes.Length - offset;
        public void Skip(int count) { if (Remaining < count) throw new InvalidDataException("Truncated packet."); offset += count; }
        public byte Byte() { Skip(1); return bytes[offset - 1]; }
        public ushort UShort() { Skip(2); return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset - 2)); }
        public int Int() { Skip(4); return BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset - 4)); }
        public string Text() {
            var end = Array.IndexOf(bytes, (byte)0, offset);
            if (end < 0 || end - offset > 4096) throw new InvalidDataException("Invalid string.");
            var text = Encoding.UTF8.GetString(bytes, offset, end - offset);
            offset = end + 1;
            return text;
        }
    }
}

public sealed class SteamPacketAssembler {
    private uint? identifier;
    private int total;
    private readonly Dictionary<int, byte[]> pieces = [];
    private uint? size;
    private uint checksum;
    public byte[]? Add(byte[] packet, CancellationToken token = default) {
        token.ThrowIfCancellationRequested();
        if (packet.Length < 5) throw new InvalidDataException("Short packet.");
        var header = BinaryPrimitives.ReadInt32LittleEndian(packet);
        if (header == -1) {
            if (identifier.HasValue) throw new InvalidDataException("Mixed packet formats.");
            return packet[4..];
        }
        if (header != -2 || packet.Length < 12) throw new InvalidDataException("Invalid split header.");
        var id = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4));
        var count = packet[8]; var index = packet[9];
        if (count is 0 or > 64 || index >= count || identifier.HasValue && (id != identifier || total != count)) throw new InvalidDataException("Invalid fragment identity.");
        identifier = id; total = count;
        var maximumSize = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(10));
        if (maximumSize == 0 || maximumSize > 65507 || packet.Length > 65507) throw new InvalidDataException("Invalid fragment size.");
        var start = 12;
        var compressed = (id & 0x80000000) != 0;
        if (compressed && index == 0) {
            if (packet.Length < 20) throw new InvalidDataException("Missing compression metadata.");
            size = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(12));
            checksum = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(16));
            if (size is 0 or > 1048576) throw new InvalidDataException("Oversized decompressed packet.");
            start = 20;
        }
        var piece = packet[start..];
        if (pieces.TryGetValue(index, out var old) && !old.SequenceEqual(piece)) throw new InvalidDataException("Conflicting fragment.");
        pieces[index] = piece;
        if (pieces.Sum(x => x.Value.Length) > 1048576) throw new InvalidDataException("Oversized response.");
        if (pieces.Count != total) return null;
        var joined = Enumerable.Range(0, total).SelectMany(i => pieces[i]).ToArray();
        if (compressed) {
            if (!size.HasValue) throw new InvalidDataException("Missing size.");
            using var input = new BZip2InputStream(new MemoryStream(joined));
            using var output = new MemoryStream();
            var buffer = new byte[4096];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0) {
                token.ThrowIfCancellationRequested();
                if (output.Length + read > size.Value) throw new InvalidDataException("Decompression limit exceeded.");
                output.Write(buffer, 0, read);
            }
            joined = output.ToArray();
            var crc = new Crc32(); crc.Update(joined);
            if (joined.Length != size || crc.Value != checksum) throw new InvalidDataException("Invalid compressed checksum or size.");
        }
        if (joined.Length >= 4 && BinaryPrimitives.ReadInt32LittleEndian(joined) == -1) joined = joined[4..];
        return joined;
    }
}
