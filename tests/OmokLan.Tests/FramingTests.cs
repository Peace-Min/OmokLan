using System.IO;
using OmokLan.Engine.Core;
using OmokLan.Engine.Net;
using Xunit;

namespace OmokLan.Tests;

public class FramingTests
{
    [Fact]
    public async Task WriteAndRead_RoundtripPreservesEnvelope()
    {
        using var stream = new MemoryStream();

        var original = new Envelope
        {
            Type = MsgKind.Move,
            X = 7,
            Y = 8,
            Seq = 1,
            Nick = "테스트플레이어"
        };

        await FrameCodec.WriteAsync(stream, original);

        stream.Position = 0;
        var decoded = await FrameCodec.ReadAsync(stream);

        Assert.NotNull(decoded);
        Assert.Equal(MsgKind.Move, decoded.Type);
        Assert.Equal(7, decoded.X);
        Assert.Equal(8, decoded.Y);
        Assert.Equal(1, decoded.Seq);
        Assert.Equal("테스트플레이어", decoded.Nick);
    }

    [Fact]
    public void BeaconPacket_SerializationAndParsing_Succeeds()
    {
        var packet = new BeaconPacket
        {
            Id = "uuid-123",
            Nick = "체스마스터",
            Avatar = 3,
            Status = PresenceStatus.Available,
            Tcp = 47801
        };

        var bytes = packet.ToBytes();
        bool ok = BeaconPacket.TryParse(bytes, out var parsed);

        Assert.True(ok);
        Assert.Equal("uuid-123", parsed.Id);
        Assert.Equal("체스마스터", parsed.Nick);
        Assert.Equal(3, parsed.Avatar);
        Assert.Equal(PresenceStatus.Available, parsed.Status);
        Assert.Equal(47801, parsed.Tcp);
    }

    [Fact]
    public void BeaconPacket_InvalidAppOrVersion_Rejected()
    {
        byte[] invalidJson = System.Text.Encoding.UTF8.GetBytes("{\"app\":\"OTHERAPP\",\"v\":1,\"type\":\"HELLO\",\"id\":\"1\"}");
        bool ok = BeaconPacket.TryParse(invalidJson, out _);
        Assert.False(ok);
    }
}
