namespace OmokLan.Engine.Core;

/// <summary>돌 색상. None 은 빈 칸을 의미합니다.</summary>
public enum Stone : byte
{
    None = 0,
    Black = 1,
    White = 2,
}

public static class StoneExtensions
{
    /// <summary>상대 색을 돌려줍니다. (None → None)</summary>
    public static Stone Opponent(this Stone stone) => stone switch
    {
        Stone.Black => Stone.White,
        Stone.White => Stone.Black,
        _ => Stone.None,
    };

    public static string ToKorean(this Stone stone) => stone switch
    {
        Stone.Black => "흑",
        Stone.White => "백",
        _ => "-",
    };
}

/// <summary>착수 시도 결과.</summary>
public enum PlaceOutcome
{
    Placed,
    Win,
    Draw,
    OutOfRange,
    Occupied,
    WrongTurn,
    GameOver,
}

public static class PlaceOutcomeExtensions
{
    /// <summary>돌이 실제로 놓였는지 여부.</summary>
    public static bool IsAccepted(this PlaceOutcome outcome) =>
        outcome is PlaceOutcome.Placed or PlaceOutcome.Win or PlaceOutcome.Draw;
}

/// <summary>
/// 오목판 상태와 규칙(자유룰: 같은 색 5개 이상 연속 시 승리, 금수 없음)을 담당합니다.
/// 좌표계: X = 열(0~14, 왼→오), Y = 행(0~14, 위→아래).
/// 스레드 안전하지 않으므로 호출 측에서 동기화해야 합니다.
/// </summary>
public sealed class OmokBoard
{
    public const int Size = 15;
    public const int WinLength = 5;

    // 승리 검사용 4방향(가로, 세로, ↘ 대각, ↗ 대각)
    private static readonly (int Dx, int Dy)[] Axes = { (1, 0), (0, 1), (1, 1), (1, -1) };

    private readonly Stone[,] _grid = new Stone[Size, Size];

    public Stone Turn { get; private set; } = Stone.Black;
    public int MoveCount { get; private set; }
    public bool IsOver { get; private set; }
    /// <summary>승자. 무승부이거나 진행 중이면 None.</summary>
    public Stone Winner { get; private set; }
    public (int X, int Y)? LastMove { get; private set; }
    public IReadOnlyList<(int X, int Y)> WinLine { get; private set; } = Array.Empty<(int, int)>();

    public Stone this[int x, int y] => _grid[x, y];

    public static bool InRange(int x, int y) => (uint)x < Size && (uint)y < Size;

    public PlaceOutcome TryPlace(int x, int y, Stone who)
    {
        if (IsOver) return PlaceOutcome.GameOver;
        if (who == Stone.None || who != Turn) return PlaceOutcome.WrongTurn;
        if (!InRange(x, y)) return PlaceOutcome.OutOfRange;
        if (_grid[x, y] != Stone.None) return PlaceOutcome.Occupied;

        _grid[x, y] = who;
        MoveCount++;
        LastMove = (x, y);

        var line = CollectWinningRun(x, y, who);
        if (line is not null)
        {
            IsOver = true;
            Winner = who;
            WinLine = line;
            return PlaceOutcome.Win;
        }

        if (MoveCount >= Size * Size)
        {
            IsOver = true;
            Winner = Stone.None;
            return PlaceOutcome.Draw;
        }

        Turn = who.Opponent();
        return PlaceOutcome.Placed;
    }

    /// <summary>기권/연결 끊김 등으로 패자를 지정해 대국을 종료합니다.</summary>
    public void Forfeit(Stone loser)
    {
        if (IsOver) return;
        IsOver = true;
        Winner = loser.Opponent();
    }

    public void Reset()
    {
        Array.Clear(_grid);
        Turn = Stone.Black;
        MoveCount = 0;
        IsOver = false;
        Winner = Stone.None;
        LastMove = null;
        WinLine = Array.Empty<(int, int)>();
    }

    /// <summary>렌더링용 복사본.</summary>
    public Stone[,] CopyGrid() => (Stone[,])_grid.Clone();

    /// <summary>방금 놓은 돌을 지나는 축 중 5개 이상 연속인 줄이 있으면 그 좌표들을 반환합니다.</summary>
    private List<(int X, int Y)>? CollectWinningRun(int originX, int originY, Stone color)
    {
        foreach (var (dx, dy) in Axes)
        {
            // 음의 방향 끝까지 이동한 다음, 양의 방향으로 훑으며 연속 구간을 수집
            int sx = originX, sy = originY;
            while (InRange(sx - dx, sy - dy) && _grid[sx - dx, sy - dy] == color)
            {
                sx -= dx;
                sy -= dy;
            }

            var run = new List<(int X, int Y)>();
            for (int cx = sx, cy = sy; InRange(cx, cy) && _grid[cx, cy] == color; cx += dx, cy += dy)
            {
                run.Add((cx, cy));
            }

            if (run.Count >= WinLength) return run;
        }

        return null;
    }
}
