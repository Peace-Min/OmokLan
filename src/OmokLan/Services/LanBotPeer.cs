using System.Net;
using OmokLan.Engine.Core;
using OmokLan.Engine.Net;
using OmokLan.Engine.Play;

namespace OmokLan.Services;

/// <summary>
/// 실제 UDP 브로드캐스트와 TCP 소켓을 통해 로비에 참여하고 대결을 진행하는 백그라운드 연습 봇입니다.
/// </summary>
public sealed class LanBotPeer : IDisposable
{
    private readonly string _botId;
    private readonly string _botNick;
    private readonly int _botAvatar;

    private DuelHost? _host;
    private LanBeacon? _beacon;
    private DuelSession? _activeSession;
    private OmokMatch? _activeMatch;
    private readonly CancellationTokenSource _cts = new();
    private bool _isRunning;

    public LanBotPeer(string nick = "🤖 알파오목 (연습 봇)", int avatar = 6)
    {
        _botId = "bot_" + Guid.NewGuid().ToString("N")[..8];
        _botNick = nick;
        _botAvatar = avatar;
    }

    public bool IsRunning => _isRunning;

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
        // 1.2초 후 자동 수락
        _ = Task.Run(async () =>
        {
            await Task.Delay(1200, _cts.Token);
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
        if (_activeMatch is null || !_activeMatch.IsMyTurn) return;

        // 봇의 차례: 1~1.8초 지연 후 착수
        _ = Task.Run(async () =>
        {
            await Task.Delay(1300, _cts.Token);
            if (_activeMatch is null || !_activeMatch.IsMyTurn) return;

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
    /// 간단한 오목 휴리스틱 알고리즘 (중앙 지향 + 공격/수비 가중치)
    /// </summary>
    private (int X, int Y)? FindBestMove(Stone[,] grid, Stone myColor)
    {
        int size = OmokBoard.Size;
        var them = myColor.Opponent();

        // 첫 수면 중앙 (7,7) 우선
        if (grid[7, 7] == Stone.None) return (7, 7);

        int bestScore = -1;
        (int X, int Y)? bestCoord = null;

        var rng = new Random();

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (grid[x, y] != Stone.None) continue;

                // 인접한 돌이 없으면 건너뜀 (속도 최적화)
                if (!HasNeighbor(grid, x, y, size)) continue;

                int attackScore = EvaluatePoint(grid, x, y, myColor, size);
                int defenseScore = EvaluatePoint(grid, x, y, them, size);

                // 수비 가중치를 높여 상대 3~4목을 방어
                int score = (attackScore * 11 / 10) + defenseScore + rng.Next(5);

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

    private int EvaluatePoint(Stone[,] grid, int x, int y, Stone color, int size)
    {
        int total = 0;
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

            if (count >= 5) total += 100000;
            else if (count == 4 && openEnds > 0) total += (openEnds == 2 ? 10000 : 2500);
            else if (count == 3 && openEnds > 0) total += (openEnds == 2 ? 1500 : 300);
            else if (count == 2 && openEnds > 0) total += 50;
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
