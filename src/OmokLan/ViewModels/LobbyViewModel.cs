using System.Collections.ObjectModel;
using System.Windows.Input;
using OmokLan.Engine.Net;
using OmokLan.Mvvm;

namespace OmokLan.ViewModels;

public sealed class PeerItemViewModel : ObservableObject
{
    private PresenceStatus _status;
    private string _nick;
    private int _avatar;

    public PeerItemViewModel(PeerInfo info)
    {
        Id = info.Id;
        _nick = info.Nick;
        _avatar = info.Avatar;
        _status = info.Status;
        Address = info.Address.ToString();
        Port = info.TcpPort;
        Peer = info;
    }

    public string Id { get; }
    public string Address { get; }
    public int Port { get; }
    public PeerInfo Peer { get; private set; }

    public string Nick
    {
        get => _nick;
        set => Set(ref _nick, value);
    }

    public int Avatar
    {
        get => _avatar;
        set => Set(ref _avatar, value);
    }

    public PresenceStatus Status
    {
        get => _status;
        set
        {
            if (Set(ref _status, value))
            {
                Raise(nameof(CanChallenge));
            }
        }
    }

    public bool CanChallenge => Status == PresenceStatus.Available;

    public void Update(PeerInfo info)
    {
        Peer = info;
        Nick = info.Nick;
        Avatar = info.Avatar;
        Status = info.Status;
    }
}

public sealed class LobbyViewModel : ObservableObject, IDisposable
{
    private readonly LanBeacon _beacon;
    private readonly DuelHost _host;
    private readonly Action<DuelSession> _onStartGame;

    private bool _isDnd;
    private bool _isChallenging;
    private string? _challengingNick;
    private CancellationTokenSource? _challengeCts;

    private IncomingChallenge? _currentIncoming;
    private string? _incomingNick;
    private int _incomingAvatar;
    private int _incomingRemainingSeconds;
    private System.Timers.Timer? _incomingTimer;

    private Services.LanBotPeer? _botPeer;
    private bool _isBotActive;

    public LobbyViewModel(LanBeacon beacon, DuelHost host, Action<DuelSession> onStartGame)
    {
        _beacon = beacon;
        _host = host;
        _onStartGame = onStartGame;

        Peers = new ObservableCollection<PeerItemViewModel>();

        _beacon.PeersChanged += OnBeaconPeersChanged;
        _host.ChallengeReceived += OnHostChallengeReceived;

        ChallengeCommand = new ActionCommand(async param =>
        {
            if (param is PeerItemViewModel item)
            {
                await StartChallengeAsync(item);
            }
        });

        CancelChallengeCommand = new ActionCommand(() =>
        {
            _challengeCts?.Cancel();
            IsChallenging = false;
        });

        AcceptIncomingCommand = new ActionCommand(async () =>
        {
            await AcceptIncomingChallengeAsync();
        });

        ToggleBotCommand = new ActionCommand(() =>
        {
            if (_botPeer is null)
            {
                _botPeer = new Services.LanBotPeer();
                _botPeer.Start();
                IsBotActive = true;
                Ui.Toast?.Invoke("🤖 연습 상대(알파오목)가 로비에 입장했습니다!");
            }
            else
            {
                _botPeer.Dispose();
                _botPeer = null;
                IsBotActive = false;
                Ui.Toast?.Invoke("🤖 연습 상대가 로비에서 퇴장했습니다.");
            }
        });

        DeclineIncomingCommand = new ActionCommand(() =>
        {
            DeclineIncomingChallenge();
        });
    }

    public ObservableCollection<PeerItemViewModel> Peers { get; }

    public bool IsDnd
    {
        get => _isDnd;
        set
        {
            if (Set(ref _isDnd, value))
            {
                _host.DoNotDisturb = value;
                _beacon.AnnounceNow();
            }
        }
    }

    public bool IsChallenging
    {
        get => _isChallenging;
        private set => Set(ref _isChallenging, value);
    }

    public string? ChallengingNick
    {
        get => _challengingNick;
        private set => Set(ref _challengingNick, value);
    }

    public bool HasIncomingChallenge => _currentIncoming is not null;

    public string? IncomingNick
    {
        get => _incomingNick;
        private set => Set(ref _incomingNick, value);
    }

    public int IncomingAvatar
    {
        get => _incomingAvatar;
        private set => Set(ref _incomingAvatar, value);
    }

    public int IncomingRemainingSeconds
    {
        get => _incomingRemainingSeconds;
        private set => Set(ref _incomingRemainingSeconds, value);
    }

    public bool IsBotActive
    {
        get => _isBotActive;
        private set => Set(ref _isBotActive, value);
    }

    public ICommand ChallengeCommand { get; }
    public ICommand CancelChallengeCommand { get; }
    public ICommand AcceptIncomingCommand { get; }
    public ICommand DeclineIncomingCommand { get; }
    public ICommand ToggleBotCommand { get; }

    public void RefreshPeers() => OnBeaconPeersChanged();

    private void OnBeaconPeersChanged()
    {
        Ui.Post(() =>
        {
            var snapshot = _beacon.Directory.Snapshot();
            var currentIds = snapshot.Select(s => s.Id).ToHashSet();

            // 제거된 피어 삭제
            for (int i = Peers.Count - 1; i >= 0; i--)
            {
                if (!currentIds.Contains(Peers[i].Id))
                {
                    Peers.RemoveAt(i);
                }
            }

            // 추가되거나 갱신된 피어 반영
            foreach (var peer in snapshot)
            {
                var existing = Peers.FirstOrDefault(p => p.Id == peer.Id);
                if (existing is not null)
                {
                    existing.Update(peer);
                }
                else
                {
                    Peers.Add(new PeerItemViewModel(peer));
                }
            }
        });
    }

    private async Task StartChallengeAsync(PeerItemViewModel item)
    {
        if (IsChallenging) return;

        IsChallenging = true;
        ChallengingNick = item.Nick;
        _challengeCts = new CancellationTokenSource();

        try
        {
            var result = await _host.ChallengeAsync(item.Peer, _challengeCts.Token);
            if (result.Outcome == ChallengeOutcome.Accepted && result.Session is not null)
            {
                _onStartGame(result.Session);
            }
            else
            {
                string msg = result.Outcome switch
                {
                    ChallengeOutcome.Declined => $"{item.Nick}님이 대결 신청을 거절했습니다.",
                    ChallengeOutcome.Busy => $"{item.Nick}님이 현재 다른 대결 중입니다.",
                    ChallengeOutcome.DoNotDisturb => $"{item.Nick}님이 방해금지 모드입니다.",
                    ChallengeOutcome.Timeout => "응답 대기 시간이 초과되었습니다.",
                    ChallengeOutcome.Unreachable => "상대방에게 연결할 수 없습니다.",
                    ChallengeOutcome.Cancelled => "대결 신청을 취소했습니다.",
                    _ => "대결 신청에 실패했습니다."
                };
                Ui.Toast?.Invoke(msg);
            }
        }
        finally
        {
            IsChallenging = false;
            ChallengingNick = null;
            _challengeCts?.Dispose();
            _challengeCts = null;
        }
    }

    private void OnHostChallengeReceived(IncomingChallenge req)
    {
        Ui.Post(() =>
        {
            _currentIncoming = req;
            IncomingNick = req.PeerNick;
            IncomingAvatar = req.PeerAvatar;
            IncomingRemainingSeconds = 20;

            Raise(nameof(HasIncomingChallenge));

            req.Withdrawn += reason =>
            {
                Ui.Post(() =>
                {
                    CloseIncoming();
                    Ui.Toast?.Invoke("대결 신청이 취소되었습니다.");
                });
            };

            _incomingTimer?.Dispose();
            _incomingTimer = new System.Timers.Timer(1000);
            _incomingTimer.Elapsed += (s, e) =>
            {
                Ui.Post(() =>
                {
                    IncomingRemainingSeconds--;
                    if (IncomingRemainingSeconds <= 0)
                    {
                        CloseIncoming();
                    }
                });
            };
            _incomingTimer.Start();
        });
    }

    private async Task AcceptIncomingChallengeAsync()
    {
        var req = _currentIncoming;
        CloseIncoming();
        if (req is null) return;

        var session = await req.AcceptAsync();
        if (session is not null)
        {
            _onStartGame(session);
        }
    }

    private void DeclineIncomingChallenge()
    {
        var req = _currentIncoming;
        CloseIncoming();
        req?.Decline();
    }

    private void CloseIncoming()
    {
        _incomingTimer?.Stop();
        _incomingTimer?.Dispose();
        _incomingTimer = null;
        _currentIncoming = null;
        Raise(nameof(HasIncomingChallenge));
    }

    public void Dispose()
    {
        _incomingTimer?.Dispose();
        _botPeer?.Dispose();
    }
}
