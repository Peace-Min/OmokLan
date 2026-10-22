using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;

namespace OmokLan.Engine.Net;

/// <summary>
/// 프레임 형식: [4바이트 Big-Endian 길이][UTF-8 JSON 본문]
/// </summary>
public static class FrameCodec
{
    public const int MaxPayload = 64 * 1024;

    public static async Task WriteAsync(Stream stream, Envelope message, CancellationToken ct = default)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(message, WireJson.Options);
        if (body.Length > MaxPayload)
            throw new InvalidDataException($"메시지가 너무 큽니다: {body.Length} bytes");

        var frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, body.Length);
        Buffer.BlockCopy(body, 0, frame, 4, body.Length);

        await stream.WriteAsync(frame, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>프레임 하나를 읽습니다. 프레임 경계에서 정상 종료(EOF)되면 null.</summary>
    public static async Task<Envelope?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        var header = new byte[4];
        if (!await ReadFullyAsync(stream, header, ct).ConfigureAwait(false))
            return null;

        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length <= 0 || length > MaxPayload)
            throw new InvalidDataException($"잘못된 프레임 길이: {length}");

        var body = new byte[length];
        if (!await ReadFullyAsync(stream, body, ct).ConfigureAwait(false))
            throw new EndOfStreamException("프레임 본문 수신 중 연결이 끊겼습니다.");

        return JsonSerializer.Deserialize<Envelope>(body, WireJson.Options)
               ?? throw new InvalidDataException("빈 메시지");
    }

    /// <summary>버퍼를 가득 채울 때까지 읽습니다. 한 바이트도 못 읽고 EOF 이면 false.</summary>
    private static async Task<bool> ReadFullyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int filled = 0;
        while (filled < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(filled), ct).ConfigureAwait(false);
            if (n == 0)
            {
                if (filled == 0) return false;
                throw new EndOfStreamException("프레임 수신 중 연결이 끊겼습니다.");
            }
            filled += n;
        }
        return true;
    }
}

/// <summary>
/// TCP 연결 하나를 감싸는 메시지 채널.
/// 내부 수신 펌프가 소켓을 단독으로 읽어 큐에 쌓으므로, 핸드셰이크 단계와 대국 단계가
/// 같은 연결을 안전하게 이어서 사용할 수 있습니다.
/// </summary>
public sealed class FramedChannel : IDisposable
{
    private readonly TcpClient _client;
    private readonly Stream _stream;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly Channel<Envelope> _inbox =
        Channel.CreateUnbounded<Envelope>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });
    private readonly CancellationTokenSource _lifetime = new();
    private int _pumpStarted;
    private int _disposed;

    public FramedChannel(TcpClient client)
    {
        _client = client;
        _client.NoDelay = true;
        _stream = client.GetStream();
        RemoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
    }

    public IPEndPoint? RemoteEndPoint { get; }

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>수신 펌프를 시작합니다. (여러 번 호출해도 1회만 동작)</summary>
    public void StartPump()
    {
        if (Interlocked.Exchange(ref _pumpStarted, 1) == 0)
            _ = PumpAsync();
    }

    private async Task PumpAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var message = await FrameCodec.ReadAsync(_stream, _lifetime.Token).ConfigureAwait(false);
                if (message is null) break;
                _inbox.Writer.TryWrite(message);
            }
        }
        catch (Exception)
        {
            // 소켓 오류/잘못된 프레임 → 채널 종료로 처리
        }
        finally
        {
            _inbox.Writer.TryComplete();
        }
    }

    /// <summary>다음 메시지를 기다립니다. 연결이 끊겼으면 null.</summary>
    public async ValueTask<Envelope?> ReceiveAsync(CancellationToken ct = default)
    {
        try
        {
            return await _inbox.Reader.ReadAsync(ct).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public async Task SendAsync(Envelope message, CancellationToken ct = default)
    {
        await _sendGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await FrameCodec.WriteAsync(_stream, message, ct).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    /// <summary>예외 없이 전송을 시도합니다. (3초 제한)</summary>
    public async Task<bool> TrySendAsync(Envelope message)
    {
        if (IsDisposed) return false;
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await SendAsync(message, limit.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        try { _client.Client.Shutdown(SocketShutdown.Both); } catch { /* 이미 닫힘 */ }
        try { _stream.Dispose(); } catch { /* 무시 */ }
        _client.Dispose();
        _inbox.Writer.TryComplete();
    }
}
