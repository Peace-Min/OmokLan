namespace OmokLan.Engine.Net;

/// <summary>
/// 승인 이후의 1:1 대국 연결. 하트비트(PING/PONG)로 연결 상태를 감시합니다.
/// <see cref="Start"/> 를 호출하기 전까지 수신 메시지는 내부 큐에 보관되므로
/// 이벤트 구독을 마친 뒤 시작하면 메시지를 놓치지 않습니다.
/// </summary>
public sealed class DuelSession : IDisposable
{
    public const string EndLocal = "local";
    public const string EndDisconnected = "disconnected";
    public const string EndTimeout = "timeout";

    private readonly FramedChannel _channel;
    private readonly CancellationTokenSource _cts = new();
    private long _lastHeardTicks;
    private int _ended;
    private int _started;

    public DuelSession(FramedChannel channel, bool isChallenger, string opponentId, string opponentNick, int opponentAvatar)
    {
        _channel = channel;
        IsChallenger = isChallenger;
        OpponentId = opponentId;
        OpponentNick = opponentNick;
        OpponentAvatar = opponentAvatar;
    }

    public bool IsChallenger { get; }
    public string OpponentId { get; }
    public string OpponentNick { get; }
    public int OpponentAvatar { get; }
    public bool IsAlive => Volatile.Read(ref _ended) == 0;

    public TimeSpan PingInterval { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan DeadAfter { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>게임 메시지 수신 (PING/PONG 제외, 백그라운드 스레드).</summary>
    public event Action<Envelope>? Received;

    /// <summary>세션 종료. 인자는 종료 사유(local/disconnected/timeout).</summary>
    public event Action<string>? Ended;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        Touch();
        _ = ReceiveLoopAsync(_cts.Token);
        _ = HeartbeatLoopAsync(_cts.Token);
    }

    private void Touch() => Interlocked.Exchange(ref _lastHeardTicks, DateTime.UtcNow.Ticks);

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                var message = await _channel.ReceiveAsync(ct).ConfigureAwait(false);
                if (message is null)
                {
                    Terminate(EndDisconnected);
                    return;
                }

                Touch();
                switch (message.Type)
                {
                    case MsgKind.Ping:
                        _ = _channel.TrySendAsync(Envelope.Of(MsgKind.Pong));
                        break;
                    case MsgKind.Pong:
                        break;
                    default:
                        Received?.Invoke(message);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 정상 종료
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(PingInterval, ct).ConfigureAwait(false);

                var silence = DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastHeardTicks), DateTimeKind.Utc);
                if (silence > DeadAfter)
                {
                    Terminate(EndTimeout);
                    return;
                }

                if (!await _channel.TrySendAsync(Envelope.Of(MsgKind.Ping)).ConfigureAwait(false))
                {
                    Terminate(EndDisconnected);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 정상 종료
        }
    }

    /// <summary>메시지 전송. 실패하면 세션을 끊김으로 종료하고 false 를 반환합니다.</summary>
    public async Task<bool> SendAsync(Envelope message)
    {
        if (!IsAlive) return false;
        if (await _channel.TrySendAsync(message).ConfigureAwait(false)) return true;
        Terminate(EndDisconnected);
        return false;
    }

    /// <summary>LEAVE 를 알린 뒤 세션을 닫습니다.</summary>
    public async Task CloseAsync()
    {
        if (!IsAlive) return;
        await _channel.TrySendAsync(Envelope.Of(MsgKind.Leave)).ConfigureAwait(false);
        Terminate(EndLocal);
    }

    private void Terminate(string reason)
    {
        if (Interlocked.Exchange(ref _ended, 1) != 0) return;
        _cts.Cancel();
        _channel.Dispose();
        Ended?.Invoke(reason);
    }

    public void Dispose() => Terminate(EndLocal);
}
