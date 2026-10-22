using System.Timers;
using OmokLan.Engine.Core;
using OmokLan.Engine.Net;

namespace OmokLan.Engine.Play;

public enum MatchEndReason
{
    FiveInRow,
    Draw,
    Resign,
    Timeout,
    OpponentLeft,
    ConnectionLost,
    ProtocolError,
}

/// <summary>대국 결과. 무승부면 Winner = None.</summary>
public sealed record MatchResult(Stone Winner, MatchEndReason Reason);

/// <summary>렌더링용 보드 스냅샷 (불변).</summary>
public sealed record BoardView(
    Stone[,] Grid,
    Stone Turn,
    (int X, int Y)? LastMove,
    IReadOnlyList<(int X, int Y)> WinLine,
    int MoveCount,
    bool IsOver,
    int TurnSecondsRemaining,
    Stone MyColor,
    bool CoinTossActive,
    Stone CoinWinnerColor,
    bool IsPaused = false);

/// <summary>
/// 채팅 메시지 항목.
/// </summary>
public sealed record ChatEntry(string Sender, string Text, bool IsMine, DateTime Time);

/// <summary>
/// 한 상대와의 대국 진행(여러 판) 을 관리합니다.
/// - 30초 턴 제한 (시간 초과 시 패배)
/// - 매 판 시작 시 코인 토스를 통해 선공(흑)/후공(백) 결정
/// - 게임 내 실시간 채팅 지원
/// - 프로필 실시간 변경 동기화 지원
/// </summary>
public sealed class OmokMatch : IDisposable
{
    public const int MaxChatLength = 200;
    public const int TurnTimeLimitSeconds = 30;

    private readonly DuelSession _session;
    private readonly object _sync = new();
    private readonly OmokBoard _board = new();
    private readonly System.Timers.Timer _turnTimer = new(1000);

    private bool _myRematchAsk;
    private bool _theirRematchAsk;
    private int _remainingSeconds = TurnTimeLimitSeconds;
    private bool _coinTossActive;
    private Stone _coinWinnerColor = Stone.None;
    private bool _isPaused;

    public OmokMatch(DuelSession session, string myNick, int myAvatar)
    {
        _session = session;
        MyNick = myNick;
        MyAvatar = myAvatar;
        OpponentNick = session.OpponentNick;
        OpponentAvatar = session.OpponentAvatar;

        _session.Received += OnPacket;
        _session.Ended += OnSessionEnded;

        _turnTimer.AutoReset = true;
        _turnTimer.Elapsed += OnTurnTimerTick;
    }

    public string MyNick { get; private set; }
    public int MyAvatar { get; private set; }
    public string OpponentNick { get; private set; }
    public int OpponentAvatar { get; private set; }
    public bool IsConnected => _session.IsAlive;
    public Stone MyColor { get; private set; } = Stone.None;
    public int Round { get; private set; } = 1;
    public MatchResult? LastResult { get; private set; }

    public bool IsPaused
    {
        get { lock (_sync) return _isPaused; }
    }

    public bool IsMyTurn
    {
        get
        {
            lock (_sync)
            {
                return !_coinTossActive && !_isPaused && !_board.IsOver && _board.Turn == MyColor && _session.IsAlive;
            }
        }
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (_isPaused || _board.IsOver || _coinTossActive) return;
            _isPaused = true;
            _turnTimer.Stop();
        }
        StateChanged?.Invoke();
    }

    public void Resume()
    {
        lock (_sync)
        {
            if (!_isPaused || _board.IsOver || _coinTossActive) return;
            _isPaused = false;
            if (_session.IsAlive)
            {
                _turnTimer.Start();
            }
        }
        StateChanged?.Invoke();
    }

    public bool AwaitingRematchReply
    {
        get { lock (_sync) return _myRematchAsk; }
    }

    public bool HasRematchOffer
    {
        get { lock (_sync) return _theirRematchAsk; }
    }

    /// <summary>상태(보드, 턴, 타이머, 코인 토스 등) 변경 알림</summary>
    public event Action? StateChanged;
    public event Action<MatchResult>? Finished;
    public event Action<ChatEntry>? ChatReceived;
    public event Action? RematchOffered;
    public event Action<bool>? RematchAnswered;
    public event Action<string>? Disconnected;
    public event Action<Stone>? CoinTossStarted;
    public event Action? OpponentProfileUpdated;

    /// <summary>대국 시작 및 코인 토스 개시</summary>
    public void Begin()
    {
        _session.Start();
        if (_session.IsChallenger)
        {
            // 신청자가 무작위로 선공(흑)을 가져갈 사람 결정 (50% 확률)
            var rng = new Random();
            var myAssignedColor = rng.Next(2) == 0 ? Stone.Black : Stone.White;
            _ = InitiateCoinTossAsync(myAssignedColor);
        }
    }

    private async Task InitiateCoinTossAsync(Stone myColor)
    {
        lock (_sync)
        {
            MyColor = myColor;
            _coinWinnerColor = myColor == Stone.Black ? MyColor : MyColor.Opponent();
            _coinTossActive = true;
        }

        // 상대에게 코인 토스 결과 전송
        await _session.SendAsync(new Envelope
        {
            Type = MsgKind.Coin,
            Round = Round,
            Color = myColor // 신청자의 색상
        }).ConfigureAwait(false);

        StartCoinTossSequence();
    }

    private void StartCoinTossSequence()
    {
        StateChanged?.Invoke();
        CoinTossStarted?.Invoke(_coinWinnerColor);

        // 연출 시간 약 3.5초 후 정식 대국 개시
        _ = Task.Run(async () =>
        {
            await Task.Delay(3500);
            lock (_sync)
            {
                _coinTossActive = false;
                ResetTurnTimerLocked();
            }
            StateChanged?.Invoke();
        });
    }

    public BoardView Snapshot()
    {
        lock (_sync)
        {
            return new BoardView(
                _board.CopyGrid(),
                _board.Turn,
                _board.LastMove,
                _board.WinLine.ToArray(),
                _board.MoveCount,
                _board.IsOver,
                _remainingSeconds,
                MyColor,
                _coinTossActive,
                _coinWinnerColor,
                _isPaused);
        }
    }

    private void ResetTurnTimerLocked()
    {
        _turnTimer.Stop();
        if (!_board.IsOver && !_coinTossActive && !_isPaused && _session.IsAlive)
        {
            _remainingSeconds = TurnTimeLimitSeconds;
            _turnTimer.Start();
        }
    }

    private void OnTurnTimerTick(object? sender, ElapsedEventArgs e)
    {
        bool timedOut = false;
        Stone currentTurn;

        lock (_sync)
        {
            if (_board.IsOver || _coinTossActive || _isPaused || !_session.IsAlive)
            {
                _turnTimer.Stop();
                return;
            }

            _remainingSeconds--;
            currentTurn = _board.Turn;

            if (_remainingSeconds <= 0)
            {
                _remainingSeconds = 0;
                _turnTimer.Stop();
                timedOut = true;
            }
        }

        StateChanged?.Invoke();

        if (timedOut)
        {
            // 시간 초과 처리
            if (currentTurn == MyColor)
            {
                // 내 턴에서 30초 초과 -> 내 시간패
                _ = HandleMyTimeoutAsync();
            }
        }
    }

    private async Task HandleMyTimeoutAsync()
    {
        lock (_sync)
        {
            if (_board.IsOver) return;
            _board.Forfeit(MyColor);
        }

        await _session.SendAsync(new Envelope { Type = MsgKind.Timeout, Color = MyColor }).ConfigureAwait(false);
        Conclude(new MatchResult(MyColor.Opponent(), MatchEndReason.Timeout));
    }

    public async Task<bool> PlaceAsync(int x, int y)
    {
        PlaceOutcome outcome;
        int sequence;
        lock (_sync)
        {
            if (!_session.IsAlive || _coinTossActive || _isPaused || _board.IsOver || _board.Turn != MyColor)
                return false;

            outcome = _board.TryPlace(x, y, MyColor);
            if (!outcome.IsAccepted()) return false;

            sequence = _board.MoveCount;
            ResetTurnTimerLocked();
        }

        StateChanged?.Invoke();
        await _session.SendAsync(new Envelope { Type = MsgKind.Move, X = x, Y = y, Seq = sequence }).ConfigureAwait(false);
        AnnounceIfDecided(outcome, MyColor);
        return true;
    }

    public async Task ResignAsync()
    {
        lock (_sync)
        {
            if (_board.IsOver) return;
            _turnTimer.Stop();
            _board.Forfeit(MyColor);
        }

        await _session.SendAsync(Envelope.Of(MsgKind.Resign)).ConfigureAwait(false);
        Conclude(new MatchResult(MyColor.Opponent(), MatchEndReason.Resign));
    }

    public async Task RequestRematchAsync()
    {
        bool acceptInstead;
        lock (_sync)
        {
            if (!_board.IsOver || !_session.IsAlive || _myRematchAsk) return;
            acceptInstead = _theirRematchAsk;
            if (!acceptInstead) _myRematchAsk = true;
        }

        if (acceptInstead)
        {
            await AnswerRematchAsync(true).ConfigureAwait(false);
            return;
        }

        StateChanged?.Invoke();
        await _session.SendAsync(Envelope.Of(MsgKind.Rematch)).ConfigureAwait(false);
    }

    public async Task AnswerRematchAsync(bool accept)
    {
        lock (_sync)
        {
            if (!_theirRematchAsk) return;
            _theirRematchAsk = false;
        }

        await _session.SendAsync(new Envelope { Type = MsgKind.RematchReply, Accept = accept }).ConfigureAwait(false);
        if (accept)
        {
            StartNextRoundRematch();
        }
        else
        {
            StateChanged?.Invoke();
        }
    }

    private void StartNextRoundRematch()
    {
        lock (_sync)
        {
            Round++;
            _board.Reset();
            _myRematchAsk = false;
            _theirRematchAsk = false;
            LastResult = null;
        }

        // 재대결 시에도 코인 토스로 선공 결정 (신청자가 주도)
        if (_session.IsChallenger)
        {
            var rng = new Random();
            var myAssignedColor = rng.Next(2) == 0 ? Stone.Black : Stone.White;
            _ = InitiateCoinTossAsync(myAssignedColor);
        }
    }

    public async Task SendChatAsync(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return;
        if (text.Length > MaxChatLength) text = text[..MaxChatLength];

        var entry = new ChatEntry(MyNick, text, true, DateTime.Now);
        ChatReceived?.Invoke(entry);

        await _session.SendAsync(new Envelope { Type = MsgKind.Chat, Text = text, Nick = MyNick }).ConfigureAwait(false);
    }

    public async Task UpdateProfileAsync(string newNick, int newAvatar)
    {
        MyNick = newNick;
        MyAvatar = newAvatar;
        await _session.SendAsync(new Envelope
        {
            Type = MsgKind.Profile,
            Nick = newNick,
            Avatar = newAvatar
        }).ConfigureAwait(false);
    }

    public Task LeaveAsync()
    {
        _turnTimer.Stop();
        return _session.CloseAsync();
    }

    private void OnPacket(Envelope packet)
    {
        switch (packet.Type)
        {
            case MsgKind.Coin:
                HandleCoinPacket(packet);
                break;

            case MsgKind.Move:
                ApplyRemoteMove(packet);
                break;

            case MsgKind.Timeout:
                HandleRemoteTimeout(packet);
                break;

            case MsgKind.Resign:
                HandleRemoteResign();
                break;

            case MsgKind.Rematch:
                HandleRematchOffer();
                break;

            case MsgKind.RematchReply:
                HandleRematchReply(packet);
                break;

            case MsgKind.Chat:
                HandleRemoteChat(packet);
                break;

            case MsgKind.Profile:
                HandleProfileUpdate(packet);
                break;

            case MsgKind.Leave:
            case MsgKind.Cancel:
                ForfeitOpponent(MatchEndReason.OpponentLeft);
                _ = DropSessionAsync("left");
                break;
        }
    }

    private void HandleCoinPacket(Envelope packet)
    {
        if (packet.Color is null) return;
        lock (_sync)
        {
            // 상대(신청자)의 색상이 왔으므로 내 색상은 상대 색의 반대
            MyColor = packet.Color.Value.Opponent();
            _coinWinnerColor = packet.Color.Value == Stone.Black ? packet.Color.Value : MyColor;
            _coinTossActive = true;
            _board.Reset();
        }
        StartCoinTossSequence();
    }

    private void HandleRemoteTimeout(Envelope packet)
    {
        Stone timedOutPlayer = packet.Color ?? MyColor.Opponent();
        lock (_sync)
        {
            if (_board.IsOver) return;
            _turnTimer.Stop();
            _board.Forfeit(timedOutPlayer);
        }

        Conclude(new MatchResult(timedOutPlayer.Opponent(), MatchEndReason.Timeout));
    }

    private void HandleRemoteResign()
    {
        bool resigned;
        lock (_sync)
        {
            resigned = !_board.IsOver;
            if (resigned)
            {
                _turnTimer.Stop();
                _board.Forfeit(MyColor.Opponent());
            }
        }
        if (resigned) Conclude(new MatchResult(MyColor, MatchEndReason.Resign));
    }

    private void ApplyRemoteMove(Envelope packet)
    {
        var them = MyColor.Opponent();
        PlaceOutcome outcome;
        lock (_sync)
        {
            bool wellFormed = packet is { X: not null, Y: not null, Seq: not null }
                              && packet.Seq == _board.MoveCount + 1
                              && _board.Turn == them;

            outcome = wellFormed ? _board.TryPlace(packet.X!.Value, packet.Y!.Value, them) : PlaceOutcome.WrongTurn;

            if (outcome.IsAccepted())
            {
                ResetTurnTimerLocked();
            }
        }

        if (!outcome.IsAccepted())
        {
            _turnTimer.Stop();
            ForfeitOpponent(MatchEndReason.ProtocolError);
            _ = DropSessionAsync("protocol");
            return;
        }

        StateChanged?.Invoke();
        AnnounceIfDecided(outcome, them);
    }

    private void HandleRematchOffer()
    {
        bool bothWant;
        lock (_sync)
        {
            if (!_board.IsOver) return;
            bothWant = _myRematchAsk;
            if (bothWant) _myRematchAsk = false;
            else _theirRematchAsk = true;
        }

        if (bothWant)
        {
            StartNextRoundRematch();
            RematchAnswered?.Invoke(true);
        }
        else
        {
            StateChanged?.Invoke();
            RematchOffered?.Invoke();
        }
    }

    private void HandleRematchReply(Envelope packet)
    {
        bool wasWaiting;
        lock (_sync)
        {
            wasWaiting = _myRematchAsk;
            _myRematchAsk = false;
        }
        if (!wasWaiting) return;

        if (packet.Accept == true)
        {
            StartNextRoundRematch();
        }
        else
        {
            StateChanged?.Invoke();
        }
        RematchAnswered?.Invoke(packet.Accept == true);
    }

    private void HandleRemoteChat(Envelope packet)
    {
        var text = (packet.Text ?? "").Trim();
        if (text.Length > MaxChatLength) text = text[..MaxChatLength];
        if (text.Length > 0)
        {
            var sender = !string.IsNullOrWhiteSpace(packet.Nick) ? packet.Nick : OpponentNick;
            ChatReceived?.Invoke(new ChatEntry(sender, text, false, DateTime.Now));
        }
    }

    private void HandleProfileUpdate(Envelope packet)
    {
        if (!string.IsNullOrWhiteSpace(packet.Nick)) OpponentNick = packet.Nick.Trim();
        if (packet.Avatar is not null) OpponentAvatar = AvatarRange.Clamp(packet.Avatar.Value);
        OpponentProfileUpdated?.Invoke();
        StateChanged?.Invoke();
    }

    private void AnnounceIfDecided(PlaceOutcome outcome, Stone mover)
    {
        if (outcome == PlaceOutcome.Win)
        {
            _turnTimer.Stop();
            Conclude(new MatchResult(mover, MatchEndReason.FiveInRow));
        }
        else if (outcome == PlaceOutcome.Draw)
        {
            _turnTimer.Stop();
            Conclude(new MatchResult(Stone.None, MatchEndReason.Draw));
        }
    }

    private void ForfeitOpponent(MatchEndReason reason)
    {
        bool applied;
        lock (_sync)
        {
            applied = !_board.IsOver;
            if (applied)
            {
                _turnTimer.Stop();
                _board.Forfeit(MyColor.Opponent());
            }
        }
        if (applied) Conclude(new MatchResult(MyColor, reason));
    }

    private void Conclude(MatchResult result)
    {
        lock (_sync) LastResult = result;
        StateChanged?.Invoke();
        Finished?.Invoke(result);
    }

    private string? _dropReason;

    private Task DropSessionAsync(string reason)
    {
        Interlocked.CompareExchange(ref _dropReason, reason, null);
        _session.Dispose();
        return Task.CompletedTask;
    }

    private void OnSessionEnded(string reason)
    {
        _turnTimer.Stop();
        var effective = Volatile.Read(ref _dropReason) ?? reason;
        if (effective != DuelSession.EndLocal)
        {
            ForfeitOpponent(MatchEndReason.ConnectionLost);
        }

        lock (_sync)
        {
            _myRematchAsk = false;
            _theirRematchAsk = false;
        }
        StateChanged?.Invoke();
        Disconnected?.Invoke(effective);
    }

    public void Dispose()
    {
        _turnTimer.Stop();
        _turnTimer.Dispose();
        _session.Received -= OnPacket;
        _session.Dispose();
    }
}
