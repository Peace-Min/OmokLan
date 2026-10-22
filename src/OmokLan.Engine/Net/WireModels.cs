using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using OmokLan.Engine.Core;

namespace OmokLan.Engine.Net;

/// <summary>사용자 상태.</summary>
public enum PresenceStatus
{
    Available,
    Dnd,
    InGame,
}

/// <summary>공용 JSON 설정 (camelCase, null 필드 생략, enum 문자열).</summary>
public static class WireJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>TCP 메시지 종류.</summary>
public static class MsgKind
{
    public const string Challenge = "CHALLENGE";
    public const string Reply = "REPLY";
    public const string Cancel = "CANCEL";
    public const string Move = "MOVE";
    public const string Resign = "RESIGN";
    public const string Rematch = "REMATCH";
    public const string RematchReply = "REMATCH_REPLY";
    public const string Chat = "CHAT";
    public const string Ping = "PING";
    public const string Pong = "PONG";
    public const string Leave = "LEAVE";
    public const string Coin = "COIN";
    public const string Timeout = "TIMEOUT";
    public const string Profile = "PROFILE";
}

/// <summary>아바타 프리셋 번호 범위.</summary>
public static class AvatarRange
{
    public const int Count = 8;
    public static int Clamp(int? id) => id is >= 0 and < Count ? id.Value : 0;
}

/// <summary>대결 신청 거절 사유 코드.</summary>
public static class RefuseCode
{
    public const string Dnd = "dnd";
    public const string Busy = "busy";
    public const string Declined = "declined";
    public const string Timeout = "timeout";
}

/// <summary>
/// TCP 로 주고받는 단일 메시지. 종류(Type)에 따라 필요한 필드만 채웁니다.
/// </summary>
public sealed class Envelope
{
    public string Type { get; set; } = "";
    public string? Id { get; set; }
    public string? Nick { get; set; }
    public int? Avatar { get; set; }
    public bool? Accept { get; set; }
    public string? Reason { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public int? Seq { get; set; }
    public int? Round { get; set; }
    /// <summary>COIN: 신청자의 색 / TIMEOUT: 시간패한 색</summary>
    public Stone? Color { get; set; }
    public string? Text { get; set; }

    public static Envelope Of(string type) => new() { Type = type };
}

/// <summary>UDP 브로드캐스트로 주기적으로 알리는 존재 신호.</summary>
public sealed class BeaconPacket
{
    public const string AppTag = "OMOKLAN";
    public const int ProtocolVersion = 1;
    public const string KindHello = "HELLO";
    public const string KindBye = "BYE";
    public const int MaxNickLength = 20;

    public string App { get; set; } = AppTag;
    public int V { get; set; } = ProtocolVersion;
    public string Type { get; set; } = KindHello;
    public string Id { get; set; } = "";
    public string Nick { get; set; } = "";
    public int Avatar { get; set; }
    public PresenceStatus Status { get; set; }
    public int Tcp { get; set; }

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, WireJson.Options);

    /// <summary>수신 바이트를 검증하며 파싱합니다. 형식이 맞지 않거나 다른 앱의 패킷이면 false.</summary>
    public static bool TryParse(ReadOnlySpan<byte> raw, out BeaconPacket packet)
    {
        packet = null!;
        if (raw.Length is 0 or > 2048) return false;

        BeaconPacket? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<BeaconPacket>(raw, WireJson.Options);
        }
        catch (JsonException)
        {
            return false;
        }

        if (parsed is null
            || parsed.App != AppTag
            || parsed.V != ProtocolVersion
            || string.IsNullOrWhiteSpace(parsed.Id) || parsed.Id.Length > 64
            || (parsed.Type != KindHello && parsed.Type != KindBye))
        {
            return false;
        }

        if (parsed.Type == KindHello)
        {
            if (string.IsNullOrWhiteSpace(parsed.Nick) || parsed.Nick.Length > MaxNickLength) return false;
            if (parsed.Tcp is < 1 or > 65535) return false;
            if (!Enum.IsDefined(parsed.Status)) return false;
        }

        packet = parsed;
        return true;
    }
}

/// <summary>탐색된 원격 사용자 정보.</summary>
public sealed record PeerInfo(string Id, string Nick, int Avatar, PresenceStatus Status, IPAddress Address, int TcpPort);
