namespace Webboard.Infrastructure.Test;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Web.Services;
using Web.Security;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.Checksum;
using Webboard.Domain.Model.Modules;

public sealed class SteamQueryTests {
    internal static byte[] Info() => [0x49, 17, .. Encoding.UTF8.GetBytes("Test server\0de_dust2\0cstrike\0Counter-Strike\0"), 10, 0, 3, 16, 0, (byte)'d', (byte)'l', 0, 1, .. Encoding.UTF8.GetBytes("1.0\0")];
    [Fact]
    public void ParsesInfoAndRejectsTruncation() {
        var info = SteamQueryClient.ParseInfo(Info());
        Assert.Equal("Test server", info.Name); Assert.Equal(3, info.Players); Assert.Equal(16, info.MaxPlayers);
        for (var length = 0; length < Info().Length; length++) Assert.Throws<InvalidDataException>(() => SteamQueryClient.ParseInfo(Info()[..length]));
    }
    private static byte[] Fragment(uint id, byte count, byte index, byte[] data) {
        byte[] header = [254, 255, 255, 255, 0, 0, 0, 0, count, index, 0xe0, 4];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), id);
        return [.. header, .. data];
    }
    [Fact]
    public void ReassemblesOutOfOrderAndRejectsConflicts() {
        var info = Info(); var assembler = new SteamPacketAssembler();
        var second = Fragment(1, 2, 1, info[10..]);
        Assert.Null(assembler.Add(second)); Assert.Null(assembler.Add(second));
        Assert.Equal(info, assembler.Add(Fragment(1, 2, 0, info[..10])));
        Assert.Throws<InvalidDataException>(() => new SteamPacketAssembler().Add(Fragment(2, 1, 2, [1])));
        assembler = new(); assembler.Add(second);
        Assert.Throws<InvalidDataException>(() => assembler.Add(Fragment(1, 2, 1, [2])));
    }
    [Fact]
    public void ValidatesCompressedSizeAndChecksum() {
        var info = Info(); using var compressed = new MemoryStream();
        BZip2.Compress(new MemoryStream(info), compressed, false, 9);
        var crc = new Crc32(); crc.Update(info);
        var metadata = new byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(metadata, (uint)info.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(metadata.AsSpan(4), (uint)crc.Value);
        var packet = Fragment(0x80000001, 1, 0, [.. metadata, .. compressed.ToArray()]);
        Assert.Equal(info, new SteamPacketAssembler().Add(packet));
        packet[16] ^= 1;
        Assert.Throws<InvalidDataException>(() => new SteamPacketAssembler().Add(packet));
    }
    [Fact]
    public async Task HandlesChallengeAndOptionalPlayerFailure() {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serve = Task.Run(async () => {
            var first = await server.ReceiveAsync(deadline.Token);
            await server.SendAsync(new byte[] { 255, 255, 255, 255, 0x41, 1, 2, 3, 4 }, first.RemoteEndPoint, deadline.Token);
            var second = await server.ReceiveAsync(deadline.Token);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, second.Buffer[^4..]);
            await server.SendAsync(new byte[] { 255, 255, 255, 255 }.Concat(Info()).ToArray(), second.RemoteEndPoint, deadline.Token);
        });
        var client = new SteamQueryClient(new NetworkPolicy(AgentSecurityTests.Config(new() { ["Outbound:AllowedPrivateNetworks:0"] = "127.0.0.0/8" })));
        var result = await client.CheckAsync("127.0.0.1", ((IPEndPoint)server.Client.LocalEndPoint!).Port, true, deadline.Token);
        await serve;
        Assert.Equal(ModuleHealthState.Healthy, result.State); Assert.NotNull(result.Steam); Assert.Null(result.Steam.PlayerDetails);
    }
    [Fact]
    public async Task UnreachableServerDoesNotThrow() {
        using var unused = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)unused.Client.LocalEndPoint!).Port; unused.Close();
        var client = new SteamQueryClient(new NetworkPolicy(AgentSecurityTests.Config(new() { ["Outbound:AllowedPrivateNetworks:0"] = "127.0.0.0/8" })));
        Assert.Equal(ModuleHealthState.Unhealthy, (await client.CheckAsync("127.0.0.1", port, false, default)).State);
    }
}
