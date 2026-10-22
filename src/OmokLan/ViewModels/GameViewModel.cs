using System.Collections.ObjectModel;
using System.Windows.Input;
using OmokLan.Engine.Core;
using OmokLan.Engine.Play;
using OmokLan.Mvvm;

namespace OmokLan.ViewModels;

public sealed class BoardCellViewModel : ObservableObject
{
    private Stone _state = Stone.None;
    private bool _isLastMove;
    private bool _isWinLine;

    public BoardCellViewModel(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int X { get; }
    public int Y { get; }

    public Stone State
    {
        get => _state;
        set => Set(ref _state, value);
    }

    public bool IsLastMove
    {
        get => _isLastMove;
        set => Set(ref _isLastMove, value);
    }

    public bool IsWinLine
    {
        get => _isWinLine;
        set => Set(ref _isWinLine, value);
    }
}

public sealed class GameViewModel : ObservableObject
{
    private readonly OmokMatch _match;
    private readonly Action _onLeave;

    private int _remainingSeconds = 30;
    private bool _isMyTurn;
    private Stone _currentTurn = Stone.Black;
    private Stone _myColor = Stone.None;
    private int _round = 1;
    private bool _isOver;
    private string _statusMessage = "게임 준비 중...";
    private string _chatInput = "";
    private bool _isCoinTossing;
    private Stone _coinWinnerColor = Stone.None;
    private string _coinWinnerDesc = "";
    private bool _hasRematchOffer;
    private bool _isAwaitingRematch;
    private string? _resultBannerText;
    private bool _isResultBannerOpen;
    private bool _isVictory;

    public GameViewModel(OmokMatch match, Action onLeave)
    {
        _match = match;
        _onLeave = onLeave;

        // 15x15 그리드 초기화
        BoardCells = new BoardCellViewModel[OmokBoard.Size, OmokBoard.Size];
        for (int y = 0; y < OmokBoard.Size; y++)
        {
            for (int x = 0; x < OmokBoard.Size; x++)
            {
                BoardCells[x, y] = new BoardCellViewModel(x, y);
            }
        }

        ChatMessages = new ObservableCollection<ChatEntry>();

        PlaceCommand = new ActionCommand(async param =>
        {
            if (param is BoardCellViewModel cell)
            {
                bool ok = await _match.PlaceAsync(cell.X, cell.Y);
                if (ok)
                {
                    BoardInvalidated?.Invoke();
                }
            }
        }, () => IsMyTurn && !IsCoinTossing && !IsOver);

        SendChatCommand = new ActionCommand(async () =>
        {
            var text = ChatInput.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;
            ChatInput = "";
            await _match.SendChatAsync(text);
        }, () => !string.IsNullOrWhiteSpace(ChatInput));

        ResignCommand = new ActionCommand(async () =>
        {
            await _match.ResignAsync();
        }, () => !IsOver && !IsCoinTossing);

        RequestRematchCommand = new ActionCommand(async () =>
        {
            await _match.RequestRematchAsync();
        }, () => IsOver && !IsAwaitingRematch);

        AcceptRematchCommand = new ActionCommand(async () =>
        {
            await _match.AnswerRematchAsync(true);
        }, () => HasRematchOffer);

        DeclineRematchCommand = new ActionCommand(async () =>
        {
            await _match.AnswerRematchAsync(false);
            HasRematchOffer = false;
        }, () => HasRematchOffer);

        LeaveCommand = new ActionCommand(async () =>
        {
            await _match.LeaveAsync();
            _onLeave();
        });

        TogglePauseCommand = new ActionCommand(() =>
        {
            if (_match.IsPaused)
            {
                _match.Resume();
            }
            else
            {
                _match.Pause();
            }
            Raise(nameof(IsPaused));
            StatusMessage = _match.IsPaused ? "⏸️ AI 대결이 일시정지되었습니다." : (IsMyTurn ? $"내 차례입니다! ({MyColor.ToKorean()})" : $"상대방({OpponentNick})이 고민 중입니다...");
        }, () => IsAiMatch && !IsOver && !IsCoinTossing);

        // 이벤트 연결
        _match.StateChanged += OnMatchStateChanged;
        _match.ChatReceived += OnMatchChatReceived;
        _match.Finished += OnMatchFinished;
        _match.RematchOffered += OnMatchRematchOffered;
        _match.RematchAnswered += OnMatchRematchAnswered;
        _match.Disconnected += OnMatchDisconnected;
        _match.CoinTossStarted += OnCoinTossStarted;
        _match.OpponentProfileUpdated += OnOpponentProfileUpdated;

        // 게임 개시
        _match.Begin();
    }

    public BoardCellViewModel[,] BoardCells { get; }
    public ObservableCollection<ChatEntry> ChatMessages { get; }

    public string MyNick => _match.MyNick;
    public int MyAvatar => _match.MyAvatar;
    public string OpponentNick => _match.OpponentNick;
    public int OpponentAvatar => _match.OpponentAvatar;

    public Stone MyColor
    {
        get => _myColor;
        private set
        {
            if (Set(ref _myColor, value))
            {
                Raise(nameof(OpponentColor));
            }
        }
    }

    public Stone OpponentColor => _myColor.Opponent();
    public bool IsPaused => _match.IsPaused;
    public bool IsAiMatch => OpponentNick.Contains("알파오목");

    public Stone CurrentTurn
    {
        get => _currentTurn;
        private set => Set(ref _currentTurn, value);
    }

    public bool IsMyTurn
    {
        get => _isMyTurn;
        private set => Set(ref _isMyTurn, value);
    }

    public int RemainingSeconds
    {
        get => _remainingSeconds;
        private set => Set(ref _remainingSeconds, value);
    }

    public int Round
    {
        get => _round;
        private set => Set(ref _round, value);
    }

    public bool IsOver
    {
        get => _isOver;
        private set => Set(ref _isOver, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }

    public string ChatInput
    {
        get => _chatInput;
        set => Set(ref _chatInput, value);
    }

    public bool IsCoinTossing
    {
        get => _isCoinTossing;
        private set => Set(ref _isCoinTossing, value);
    }

    public Stone CoinWinnerColor
    {
        get => _coinWinnerColor;
        private set => Set(ref _coinWinnerColor, value);
    }

    public string CoinWinnerDesc
    {
        get => _coinWinnerDesc;
        private set => Set(ref _coinWinnerDesc, value);
    }

    public bool HasRematchOffer
    {
        get => _hasRematchOffer;
        private set => Set(ref _hasRematchOffer, value);
    }

    public bool IsAwaitingRematch
    {
        get => _isAwaitingRematch;
        private set => Set(ref _isAwaitingRematch, value);
    }

    public string? ResultBannerText
    {
        get => _resultBannerText;
        private set => Set(ref _resultBannerText, value);
    }

    public bool IsResultBannerOpen
    {
        get => _isResultBannerOpen;
        private set => Set(ref _isResultBannerOpen, value);
    }

    public bool IsVictory
    {
        get => _isVictory;
        private set => Set(ref _isVictory, value);
    }

    public event Action? BoardInvalidated;
    public event Action<Stone>? CoinTossTriggered;

    public ICommand PlaceCommand { get; }
    public ICommand SendChatCommand { get; }
    public ICommand ResignCommand { get; }
    public ICommand RequestRematchCommand { get; }
    public ICommand AcceptRematchCommand { get; }
    public ICommand DeclineRematchCommand { get; }
    public ICommand LeaveCommand { get; }
    public ICommand TogglePauseCommand { get; }

    public void NotifyProfileChanged()
    {
        Raise(nameof(MyNick));
        Raise(nameof(MyAvatar));
        _ = _match.UpdateProfileAsync(_match.MyNick, _match.MyAvatar);
    }

    private void OnCoinTossStarted(Stone winnerColor)
    {
        Ui.Post(() =>
        {
            MyColor = _match.MyColor;
            IsCoinTossing = true;
            CoinWinnerColor = winnerColor;
            bool iAmBlack = _match.MyColor == Stone.Black;
            CoinWinnerDesc = iAmBlack
                ? $"코인 토스 승리! 흑(선공)으로 시작합니다."
                : $"상대방 선공! 백(후공)으로 시작합니다.";
            StatusMessage = "코인 토스로 선공(흑)을 결정하는 중입니다...";
            CoinTossTriggered?.Invoke(_match.MyColor);
        });
    }

    private void OnOpponentProfileUpdated()
    {
        Ui.Post(() =>
        {
            Raise(nameof(OpponentNick));
            Raise(nameof(OpponentAvatar));
        });
    }

    private void OnMatchStateChanged()
    {
        Ui.Post(() =>
        {
            var snap = _match.Snapshot();

            MyColor = snap.MyColor;
            CurrentTurn = snap.Turn;
            IsMyTurn = _match.IsMyTurn;
            RemainingSeconds = snap.TurnSecondsRemaining;
            Round = _match.Round;
            IsOver = snap.IsOver;
            IsCoinTossing = snap.CoinTossActive;
            HasRematchOffer = _match.HasRematchOffer;
            IsAwaitingRematch = _match.AwaitingRematchReply;

            // 보드 상태 동기화
            var winSet = new HashSet<(int X, int Y)>(snap.WinLine);
            for (int y = 0; y < OmokBoard.Size; y++)
            {
                for (int x = 0; x < OmokBoard.Size; x++)
                {
                    var cell = BoardCells[x, y];
                    cell.State = snap.Grid[x, y];
                    cell.IsLastMove = snap.LastMove.HasValue && snap.LastMove.Value.X == x && snap.LastMove.Value.Y == y;
                    cell.IsWinLine = winSet.Contains((x, y));
                }
            }

            // 착수 즉시 0ms 화면 재렌더링 트리거
            BoardInvalidated?.Invoke();
            Raise(nameof(IsPaused));

            if (!snap.IsOver && !snap.CoinTossActive)
            {
                IsResultBannerOpen = false;
                if (_match.IsPaused)
                {
                    StatusMessage = "⏸️ AI 대결이 일시정지되었습니다.";
                }
                else
                {
                    StatusMessage = IsMyTurn
                        ? $"내 차례입니다! ({MyColor.ToKorean()}) - 30초 내 착수하세요."
                        : $"상대방({OpponentNick})이 고민 중입니다...";
                }
            }
        });
    }

    private void OnMatchChatReceived(ChatEntry entry)
    {
        Ui.Post(() =>
        {
            ChatMessages.Add(entry);
        });
    }

    private void OnMatchFinished(MatchResult result)
    {
        Ui.Post(() =>
        {
            IsOver = true;
            IsResultBannerOpen = true;

            if (result.Winner == Stone.None)
            {
                ResultBannerText = "무승부입니다!";
                IsVictory = false;
            }
            else
            {
                bool win = result.Winner == MyColor;
                IsVictory = win;

                string reasonText = result.Reason switch
                {
                    MatchEndReason.FiveInRow => "5목 완성!",
                    MatchEndReason.Timeout => win ? "상대방 30초 시간 초과 승리!" : "30초 시간 초과 패배!",
                    MatchEndReason.Resign => win ? "상대방 기권 승리!" : "기권 패배!",
                    MatchEndReason.OpponentLeft => "상대방 퇴장으로 승리!",
                    MatchEndReason.ConnectionLost => "상대방 연결 끊김으로 승리!",
                    _ => ""
                };

                ResultBannerText = win ? $"승리했습니다! 🏆 ({reasonText})" : $"패배했습니다. 💔 ({reasonText})";
            }

            StatusMessage = ResultBannerText;
        });
    }

    private void OnMatchRematchOffered()
    {
        Ui.Post(() =>
        {
            HasRematchOffer = true;
            Ui.Toast?.Invoke($"{OpponentNick}님이 재대결을 신청했습니다!");
        });
    }

    private void OnMatchRematchAnswered(bool accepted)
    {
        Ui.Post(() =>
        {
            IsAwaitingRematch = false;
            if (!accepted)
            {
                Ui.Toast?.Invoke("상대방이 재대결을 거절했습니다.");
            }
        });
    }

    private void OnMatchDisconnected(string reason)
    {
        Ui.Post(() =>
        {
            StatusMessage = "상대방과의 연결이 끊겼습니다.";
            Ui.Toast?.Invoke("상대방과의 연결이 종료되었습니다.");
        });
    }
}
