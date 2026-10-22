using System.Net;
using OmokLan.Engine.Core;
using OmokLan.Engine.Net;
using OmokLan.Engine.Play;

namespace OmokLan.Services;

public enum AiDifficulty
{
    Easy,   // 초급
    Normal, // 중급
    Hard    // 고급
}

/// <summary>
/// 실제 UDP 브로드캐스트와 TCP 소켓을 통해 로비에 참여하고 대결을 진행하는 백그라운드 연습 봇입니다.
/// </summary>
public sealed class LanBotPeer : IDisposable
{
    private readonly string _botId;
    private readonly string _botNick;
    private readonly int _botAvatar;
    private readonly AiDifficulty _difficulty;

    private DuelHost? _host;
    private LanBeacon? _beacon;
    private DuelSession? _activeSession;
    private OmokMatch? _activeMatch;
    private readonly CancellationTokenSource _cts = new();
    private bool _isRunning;

    public LanBotPeer(AiDifficulty difficulty = AiDifficulty.Normal, int avatar = 6)
    {
        _difficulty = difficulty;
        string diffLabel = difficulty switch
        {
            AiDifficulty.Easy => "초급",
            AiDifficulty.Hard => "고급",
            _ => "중급"
        };
        _botId = "bot_" + Guid.NewGuid().ToString("N")[..8];
        _botNick = $"🤖 알파오목 [{diffLabel}]";
        _botAvatar = avatar;
    }

    public bool IsRunning => _isRunning;
    public AiDifficulty Difficulty => _difficulty;

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;

        _host = new DuelHost(_botId, _botNick)
        {
            SelfAvatar = _botAvatar
        };
        _host.Start(basePort: 0);

        _host.ChallengeReceived += OnChallengeReceived;

        _beacon = new LanBeacon(_botId, () =>
        {
            var status = _host.InGame || _host.Engaged ? PresenceStatus.InGame : PresenceStatus.Available;
            return (_botNick, _botAvatar, status, _host.Port);
        });
        _beacon.Start();
    }

    private void OnChallengeReceived(IncomingChallenge req)
    {
        // 1초 후 자동 수락
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000, _cts.Token);
            var session = await req.AcceptAsync();
            if (session is not null)
            {
                StartMatch(session);
            }
        });
    }

    private void StartMatch(DuelSession session)
    {
        _activeSession = session;
        _activeMatch = new OmokMatch(session, _botNick, _botAvatar);

        _activeMatch.StateChanged += OnMatchStateChanged;
        _activeMatch.ChatReceived += OnChatReceived;
        _activeMatch.Finished += OnMatchFinished;
        _activeMatch.RematchOffered += OnRematchOffered;

        _activeMatch.Begin();
    }

    private void OnMatchStateChanged()
    {
        if (_activeMatch is null || !_activeMatch.IsMyTurn || _activeMatch.IsPaused) return;

        // 봇의 차례: 일시정지 중이면 대기 후 착수
        _ = Task.Run(async () =>
        {
            await Task.Delay(1100, _cts.Token);

            while (_activeMatch is not null && _activeMatch.IsPaused && !_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(250, _cts.Token);
            }

            if (_activeMatch is null || !_activeMatch.IsMyTurn || _activeMatch.IsPaused) return;

            var snap = _activeMatch.Snapshot();
            var move = FindBestMove(snap.Grid, _activeMatch.MyColor);
            if (move.HasValue)
            {
                await _activeMatch.PlaceAsync(move.Value.X, move.Value.Y);
            }
        });
    }

    private void OnChatReceived(ChatEntry entry)
    {
        if (entry.IsMine) return;

        // 채팅에 1초 후 자동 반응
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000, _cts.Token);
            if (_activeMatch is null || !_activeMatch.IsConnected) return;

            string[] replies =
            {
                "좋은 수네요! 집중해서 두겠습니다.",
                "재미있는 대결입니다! 😊",
                "만만치 않으시군요!",
                "멋진 착수입니다.",
                "끝까지 최선을 다해보겠습니다!"
            };
            var rng = new Random();
            var reply = replies[rng.Next(replies.Length)];
            await _activeMatch.SendChatAsync(reply);
        });
    }

    private void OnRematchOffered()
    {
        // 상대방이 재대결 요청 시 1.5초 후 수락
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500, _cts.Token);
            if (_activeMatch is not null)
            {
                await _activeMatch.AnswerRematchAsync(true);
            }
        });
    }

    private void OnMatchFinished(MatchResult result)
    {
        // 경기 종료 시 축하/격려 채팅
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000, _cts.Token);
            if (_activeMatch is not null && _activeMatch.IsConnected)
            {
                string endMsg = result.Winner == _activeMatch.MyColor
                    ? "좋은 승부였습니다! 한 판 더 하시겠어요?"
                    : "축하드립니다! 완패했네요 👏";
                await _activeMatch.SendChatAsync(endMsg);
            }
        });
    }

    /// <summary>
    /// 난이도별 오목 AI 수 탐색 알고리즘
    /// </summary>
    private (int X, int Y)? FindBestMove(Stone[,] grid, Stone myColor)
    {
        int size = OmokBoard.Size;
        var them = myColor.Opponent();

        // 첫 수면 중앙 (7,7) 우선
        if (grid[7, 7] == Stone.None) return (7, 7);

        var rng = new Random();

        // [초급 모드]: 35% 확률로 주변 빈자리 무작위 착수 (초보자 배려 실수 유도)
        if (_difficulty == AiDifficulty.Easy && rng.Next(100) < 35)
        {
            var candidates = new List<(int X, int Y)>();
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (grid[x, y] == Stone.None && HasNeighbor(grid, x, y, size))
                    {
                        candidates.Add((x, y));
                    }
                }
            }
            if (candidates.Count > 0)
            {
                return candidates[rng.Next(candidates.Count)];
            }
        }

        int bestScore = -1;
        (int X, int Y)? bestCoord = null;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (grid[x, y] != Stone.None) continue;

                // 인접한 돌이 없으면 건너뜀 (연산 최적화)
                if (!HasNeighbor(grid, x, y, size)) continue;

                int attackScore = EvaluatePoint(grid, x, y, myColor, size, _difficulty);
                int defenseScore = EvaluatePoint(grid, x, y, them, size, _difficulty);

                int score;
                if (_difficulty == AiDifficulty.Hard)
                {
                    // 고급: 공격 1.25배, 철통 수비, 중앙/화점 포지셔닝 가산
                    int centerBonus = 14 - (Math.Abs(x - 7) + Math.Abs(y - 7));
                    score = (int)(attackScore * 1.25) + defenseScore + centerBonus;
                }
                else if (_difficulty == AiDifficulty.Easy)
                {
                    // 초급: 단순 합산 및 노이즈
                    score = attackScore + (defenseScore * 7 / 10) + rng.Next(20);
                }
                else
                {
                    // 중급: 정석 밸런스
                    score = (attackScore * 11 / 10) + defenseScore + rng.Next(5);
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestCoord = (x, y);
                }
            }
        }

        return bestCoord ?? (7, 7);
    }

    private bool HasNeighbor(Stone[,] grid, int cx, int cy, int size)
    {
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = cx + dx, ny = cy + dy;
                if ((uint)nx < size && (uint)ny < size && grid[nx, ny] != Stone.None)
                    return true;
            }
        }
        return false;
    }

    private int EvaluatePoint(Stone[,] grid, int x, int y, Stone color, int size, AiDifficulty diff)
    {
        int total = 0;
        int openThrees = 0;
        int fours = 0;

        (int Dx, int Dy)[] dirs = { (1, 0), (0, 1), (1, 1), (1, -1) };

        foreach (var (dx, dy) in dirs)
        {
            int count = 1;
            int openEnds = 0;

            // 정방향
            int step = 1;
            while (true)
            {
                int nx = x + dx * step, ny = y + dy * step;
                if ((uint)nx >= size || (uint)ny >= size) break;
                if (grid[nx, ny] == color) count++;
                else
                {
                    if (grid[nx, ny] == Stone.None) openEnds++;
                    break;
                }
                step++;
            }

            // 역방향
            step = 1;
            while (true)
            {
                int nx = x - dx * step, ny = y - dy * step;
                if ((uint)nx >= size || (uint)ny >= size) break;
                if (grid[nx, ny] == color) count++;
                else
                {
                    if (grid[nx, ny] == Stone.None) openEnds++;
                    break;
                }
                step++;
            }

            if (count >= 5)
            {
                total += 1000000; // 5목 승리 (최우선)
            }
            else if (count == 4)
            {
                fours++;
                if (openEnds == 2) total += 120000; // 양쪽 열린 4목 (필승)
                else if (openEnds == 1) total += (diff == AiDifficulty.Hard ? 18000 : 2500); // 닫힌 4목
            }
            else if (count == 3)
            {
                if (openEnds == 2)
                {
                    openThrees++;
                    total += (diff == AiDifficulty.Hard ? 12000 : 1500); // 열린 3목
                }
                else if (openEnds == 1) total += 300;
            }
            else if (count == 2 && openEnds > 0)
            {
                total += (openEnds == 2 ? 120 : 40);
            }
        }

        // 고급 모드: 쌍삼(3-3) 및 4-3 복합 승리 패턴 가중치 대폭 추가
        if (diff == AiDifficulty.Hard)
        {
            if (openThrees >= 2) total += 70000; // 더블 3목 (필승 수)
            if (fours >= 1 && openThrees >= 1) total += 90000; // 4-3 공격/방어
        }

        return total;
    }

    public void Dispose()
    {
        _isRunning = false;
        _cts.Cancel();
        _beacon?.Dispose();
        _host?.Dispose();
        _activeMatch?.Dispose();
    }
}
