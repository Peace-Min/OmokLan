using System.Net;
using System.Net.Sockets;

namespace OmokLan.Engine.Net;

/// <summary>대결 신청 결과 종류.</summary>
public enum ChallengeOutcome
{
    Accepted,
    Declined,
    Busy,
    DoNotDisturb,
    Timeout,
    Unreachable,
    Cancelled,
    LocalBusy,
    Failed,
}

public sealed record ChallengeResult(ChallengeOutcome Outcome, DuelSession? Session = null);

/// <summary>
/// 수신된 대결 신청. <see cref="DuelHost.ChallengeReceived"/> 핸들러 안에서
/// <see cref="Withdrawn"/> 을 동기적으로 구독해야 철회 알림을 놓치지 않습니다.
/// </summary>
public sealed class IncomingChallenge
{
    internal IncomingChallenge(string peerId, string peerNick, int peerAvatar, IPAddress? address, DateTime expiresAtUtc)
    {
        PeerId = peerId;
        PeerNick = peerNick;
        PeerAvatar = peerAvatar;
        Address = address;
        ExpiresAtUtc = expiresAtUtc;
    }

    public string PeerId { get; }
    public string PeerNick { get; }
    public int PeerAvatar { get; }
    public IPAddress? Address { get; }
    public DateTime ExpiresAtUtc { get; }

    internal TaskCompletionSource<bool> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<DuelSession?> Outcome { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>신청자가 취소했거나, 시간 초과/연결 끊김으로 무효가 된 경우. (cancelled/timeout/disconnected)</summary>
    public event Action<string>? Withdrawn;

    internal void NotifyWithdrawn(string reason) => Withdrawn?.Invoke(reason);

    /// <summary>수락. 성공하면 시작 전 상태의 세션을, 이미 무효가 되었으면 null.</summary>
    public Task<DuelSession?> AcceptAsync()
    {
        Decision.TrySetResult(true);
        return Outcome.Task;
    }

    public void Decline() => Decision.TrySetResult(false);
}

/// <summary>
/// TCP 리스너 + 대결 신청/응답 핸드셰이크를 담당합니다.
/// 동시에 하나의 대결(대기 중 신청 포함)만 허용하고, 방해금지/게임중이면 자동 거절합니다.
/// </summary>
public sealed class DuelHost : IDisposable
{
    public const int DefaultBasePort = 47801;
    public const int DefaultPortSpan = 10;

    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private TcpListener? _listener;
    private IncomingChallenge? _pendingIncoming;
    private bool _outgoingInFlight;
    private DuelSession? _active;
    private volatile bool _doNotDisturb;
    private volatile bool _engaged;

    public DuelHost(string selfId, string selfNick)
    {
        SelfId = selfId;
        SelfNick = selfNick;
    }

    public string SelfId { get; }
    public string SelfNick { get; set; }
    public int SelfAvatar { get; set; }
    public int Port { get; private set; }

    public bool DoNotDisturb
    {
        get => _doNotDisturb;
        set => _doNotDisturb = value;
    }

    /// <summary>UI 가 게임 화면(결과 확인 포함)에 머무는 동안 true → 신규 신청을 busy 로 자동 거절.</summary>
    public bool Engaged
    {
        get => _engaged;
        set => _engaged = value;
    }

    public TimeSpan DecisionTimeout { get; set; } = TimeSpan.FromSeconds(20);
    public TimeSpan ReplyTimeout { get; set; } = TimeSpan.FromSeconds(25);
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(4);

    /// <summary>새 대결 신청 수신 (백그라운드 스레드).</summary>
    public event Action<IncomingChallenge>? ChallengeReceived;

    public bool IsBusy
    {
        get { lock (_gate) return IsBusyLocked; }
    }

    public bool InGame
    {
        get { lock (_gate) return _active is not null; }
    }

    private bool IsBusyLocked => _engaged || _pendingIncoming is not null || _outgoingInFlight || _active is not null;

    /// <summary>basePort 부터 span 개 포트를 차례로 시도해 리스닝을 시작합니다. (basePort=0 이면 OS 임의 포트)</summary>
    public void Start(int basePort = DefaultBasePort, int span = DefaultPortSpan)
    {
        TcpListener? bound = null;
        int attempts = basePort == 0 ? 1 : Math.Max(1, span);
        for (int i = 0; i < attempts && bound is null; i++)
        {
            var candidate = new TcpListener(IPAddress.Any, basePort == 0 ? 0 : basePort + i)
            {
                ExclusiveAddressUse = true,
            };
            try
            {
                candidate.Start();
                bound = candidate;
            }
            catch (SocketException)
            {
                // 이미 사용 중 → 다음 포트
            }
        }

        if (bound is null)
        {
            bound = new TcpListener(IPAddress.Any, 0);
            bound.Start();
        }

        _listener = bound;
        Port = ((IPEndPoint)bound.LocalEndpoint).Port;
        _ = AcceptLoopAsync(_stop.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        var listener = _listener!;
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { continue; }

            _ = Task.Run(() => HandleInboundAsync(client), ct);
        }
    }

    private async Task HandleInboundAsync(TcpClient client)
    {
        var channel = new FramedChannel(client);
        channel.StartPump();

        Envelope? opening;
        try
        {
            using var firstWait = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            opening = await channel.ReceiveAsync(firstWait.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            opening = null;
        }

        if (opening is not { Type: MsgKind.Challenge } || string.IsNullOrWhiteSpace(opening.Id))
        {
            channel.Dispose();
            return;
        }

        string peerNick = string.IsNullOrWhiteSpace(opening.Nick) ? "알 수 없음" : opening.Nick.Trim();
        if (peerNick.Length > BeaconPacket.MaxNickLength) peerNick = peerNick[..BeaconPacket.MaxNickLength];

        IncomingChallenge? request = null;
        string? autoRefusal;
        lock (_gate)
        {
            if (_doNotDisturb) autoRefusal = RefuseCode.Dnd;
            else if (IsBusyLocked) autoRefusal = RefuseCode.Busy;
            else
            {
                autoRefusal = null;
                request = new IncomingChallenge(opening.Id, peerNick, AvatarRange.Clamp(opening.Avatar),
                    channel.RemoteEndPoint?.Address, DateTime.UtcNow + DecisionTimeout);
                _pendingIncoming = request;
            }
        }

        if (autoRefusal is not null)
        {
            await channel.TrySendAsync(new Envelope { Type = MsgKind.Reply, Accept = false, Reason = autoRefusal })
                .ConfigureAwait(false);
            channel.Dispose();
            return;
        }

        await AwaitDecisionAsync(channel, request!).ConfigureAwait(false);
    }

    /// <summary>사용자 결정 / 신청자 취소·끊김 / 시간 초과 중 먼저 일어나는 것을 처리합니다.</summary>
    private async Task AwaitDecisionAsync(FramedChannel channel, IncomingChallenge request)
    {
        DuelSession? session = null;
        string? withdrawnReason = null;

        try
        {
            ChallengeReceived?.Invoke(request);

            using var expiry = new CancellationTokenSource(DecisionTimeout);
            var remoteTask = channel.ReceiveAsync(expiry.Token).AsTask();
            var first = await Task.WhenAny(request.Decision.Task, remoteTask).ConfigureAwait(false);

            if (first == request.Decision.Task)
            {
                expiry.Cancel();
                bool remoteArrived = false;
                Envelope? lateMessage = null;
                try
                {
                    lateMessage = await remoteTask.ConfigureAwait(false);
                    remoteArrived = true;
                }
                catch (OperationCanceledException) { }

                if (remoteArrived)
                {
                    // 결정과 거의 동시에 신청자가 취소했거나 연결이 끊긴 경우 → 수락하지 않음
                    withdrawnReason = lateMessage?.Type == MsgKind.Cancel ? "cancelled" : DuelSession.EndDisconnected;
                }
                else if (request.Decision.Task.Result)
                {
                    bool sent = await channel.TrySendAsync(new Envelope { Type = MsgKind.Reply, Accept = true })
                        .ConfigureAwait(false);
                    if (sent)
                    {
                        session = CreateSession(channel, isChallenger: false, request.PeerId, request.PeerNick, request.PeerAvatar);
                    }
                    else
                    {
                        withdrawnReason = DuelSession.EndDisconnected;
                    }
                }
                else
                {
                    await channel.TrySendAsync(new Envelope { Type = MsgKind.Reply, Accept = false, Reason = RefuseCode.Declined })
                        .ConfigureAwait(false);
                }
            }
            else
            {
                Envelope? remote;
                try { remote = await remoteTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { remote = null; withdrawnReason = RefuseCode.Timeout; }

                if (withdrawnReason == RefuseCode.Timeout)
                {
                    await channel.TrySendAsync(new Envelope { Type = MsgKind.Reply, Accept = false, Reason = RefuseCode.Timeout })
                        .ConfigureAwait(false);
                }
                else
                {
                    withdrawnReason = remote?.Type == MsgKind.Cancel ? "cancelled" : DuelSession.EndDisconnected;
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_pendingIncoming, request)) _pendingIncoming = null;
            }

            if (session is null) channel.Dispose();
            request.Decision.TrySetResult(false);
            request.Outcome.TrySetResult(session);
            if (withdrawnReason is not null) request.NotifyWithdrawn(withdrawnReason);
        }
    }

    /// <summary>상대에게 대결을 신청하고 응답을 기다립니다.</summary>
    public async Task<ChallengeResult> ChallengeAsync(PeerInfo target, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (IsBusyLocked) return new ChallengeResult(ChallengeOutcome.LocalBusy);
            _outgoingInFlight = true;
        }

        FramedChannel? channel = null;
        DuelSession? session = null;
        try
        {
            var client = new TcpClient(AddressFamily.InterNetwork);
            try
            {
                using var connectLimit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectLimit.CancelAfter(ConnectTimeout);
                await client.ConnectAsync(target.Address, target.TcpPort, connectLimit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                client.Dispose();
                return new ChallengeResult(ChallengeOutcome.Cancelled);
            }
            catch (Exception)
            {
                client.Dispose();
                return new ChallengeResult(ChallengeOutcome.Unreachable);
            }

            channel = new FramedChannel(client);
            channel.StartPump();

            if (!await channel.TrySendAsync(new Envelope
                    {
                        Type = MsgKind.Challenge, Id = SelfId, Nick = SelfNick, Avatar = SelfAvatar,
                    })
                    .ConfigureAwait(false))
            {
                return new ChallengeResult(ChallengeOutcome.Unreachable);
            }

            using var replyLimit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            replyLimit.CancelAfter(ReplyTimeout);
            try
            {
                while (true)
                {
                    var message = await channel.ReceiveAsync(replyLimit.Token).ConfigureAwait(false);
                    if (message is null) return new ChallengeResult(ChallengeOutcome.Failed);
                    if (message.Type != MsgKind.Reply) continue;

                    if (message.Accept == true)
                    {
                        session = CreateSession(channel, isChallenger: true, target.Id, target.Nick, target.Avatar);
                        return new ChallengeResult(ChallengeOutcome.Accepted, session);
                    }

                    return new ChallengeResult(message.Reason switch
                    {
                        RefuseCode.Dnd => ChallengeOutcome.DoNotDisturb,
                        RefuseCode.Busy => ChallengeOutcome.Busy,
                        RefuseCode.Timeout => ChallengeOutcome.Timeout,
                        _ => ChallengeOutcome.Declined,
                    });
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await channel.TrySendAsync(Envelope.Of(MsgKind.Cancel)).ConfigureAwait(false);
                return new ChallengeResult(ChallengeOutcome.Cancelled);
            }
            catch (OperationCanceledException)
            {
                await channel.TrySendAsync(Envelope.Of(MsgKind.Cancel)).ConfigureAwait(false);
                return new ChallengeResult(ChallengeOutcome.Timeout);
            }
        }
        finally
        {
            lock (_gate) _outgoingInFlight = false;
            if (session is null) channel?.Dispose();
        }
    }

    private DuelSession CreateSession(FramedChannel channel, bool isChallenger, string peerId, string peerNick, int peerAvatar)
    {
        var session = new DuelSession(channel, isChallenger, peerId, peerNick, peerAvatar);
        session.Ended += _ =>
        {
            lock (_gate)
            {
                if (ReferenceEquals(_active, session)) _active = null;
            }
        };
        lock (_gate)
        {
            _active = session;
            _pendingIncoming = null;
        }
        return session;
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _listener?.Stop(); } catch { /* 무시 */ }
        DuelSession? active;
        lock (_gate) active = _active;
        active?.Dispose();
    }
}
