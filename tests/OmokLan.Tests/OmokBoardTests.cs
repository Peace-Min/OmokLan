using OmokLan.Engine.Core;
using Xunit;

namespace OmokLan.Tests;

public class OmokBoardTests
{
    [Fact]
    public void HorizontalFiveInRow_DeclaresWinner()
    {
        var board = new OmokBoard();
        // 흑이 (3,7) ~ (7,7) 에 5목 완성
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(PlaceOutcome.Placed, board.TryPlace(3 + i, 7, Stone.Black));
            Assert.Equal(PlaceOutcome.Placed, board.TryPlace(3 + i, 8, Stone.White));
        }

        var finalMove = board.TryPlace(7, 7, Stone.Black);
        Assert.Equal(PlaceOutcome.Win, finalMove);
        Assert.True(board.IsOver);
        Assert.Equal(Stone.Black, board.Winner);
        Assert.Equal(5, board.WinLine.Count);
    }

    [Fact]
    public void VerticalFiveInRow_DeclaresWinner()
    {
        var board = new OmokBoard();
        // 백이 (5,3) ~ (5,7) 에 5목 완성
        board.TryPlace(0, 0, Stone.Black); // 흑

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(PlaceOutcome.Placed, board.TryPlace(5, 3 + i, Stone.White));
            Assert.Equal(PlaceOutcome.Placed, board.TryPlace(1, i, Stone.Black));
        }

        var finalMove = board.TryPlace(5, 7, Stone.White);
        Assert.Equal(PlaceOutcome.Win, finalMove);
        Assert.True(board.IsOver);
        Assert.Equal(Stone.White, board.Winner);
    }

    [Fact]
    public void DiagonalFiveInRow_DeclaresWinner()
    {
        var board = new OmokBoard();
        // 흑이 (2,2), (3,3), (4,4), (5,5), (6,6) 대각선
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(PlaceOutcome.Placed, board.TryPlace(2 + i, 2 + i, Stone.Black));
            Assert.Equal(PlaceOutcome.Placed, board.TryPlace(0, i + 1, Stone.White));
        }

        var finalMove = board.TryPlace(6, 6, Stone.Black);
        Assert.Equal(PlaceOutcome.Win, finalMove);
        Assert.True(board.IsOver);
        Assert.Equal(Stone.Black, board.Winner);
    }

    [Fact]
    public void SixInRow_FreeRuleAllowsWin()
    {
        var board = new OmokBoard();
        // 자유룰: 6목 이상도 승리로 인정
        board.TryPlace(0, 0, Stone.Black);
        board.TryPlace(0, 14, Stone.White);
        board.TryPlace(1, 0, Stone.Black);
        board.TryPlace(1, 14, Stone.White);
        board.TryPlace(2, 0, Stone.Black);
        board.TryPlace(2, 14, Stone.White);
        board.TryPlace(3, 0, Stone.Black);
        board.TryPlace(3, 13, Stone.White); // 다른 위치
        board.TryPlace(5, 0, Stone.Black); // 4는 건너뜀
        board.TryPlace(4, 14, Stone.White);

        // 4 자리에 흑을 놓아 6연속 완성
        var outcome = board.TryPlace(4, 0, Stone.Black);
        Assert.Equal(PlaceOutcome.Win, outcome);
        Assert.Equal(Stone.Black, board.Winner);
        Assert.True(board.WinLine.Count >= 5);
    }

    [Fact]
    public void OutOfRangeAndOccupied_Rejected()
    {
        var board = new OmokBoard();
        Assert.Equal(PlaceOutcome.OutOfRange, board.TryPlace(-1, 0, Stone.Black));
        Assert.Equal(PlaceOutcome.OutOfRange, board.TryPlace(15, 7, Stone.Black));

        Assert.Equal(PlaceOutcome.Placed, board.TryPlace(7, 7, Stone.Black));
        // 같은 자리에 다시 두면 Occupied
        Assert.Equal(PlaceOutcome.Occupied, board.TryPlace(7, 7, Stone.White));
    }

    [Fact]
    public void WrongTurn_Rejected()
    {
        var board = new OmokBoard();
        // 흑 선공인데 백이 먼저 두면 WrongTurn
        Assert.Equal(PlaceOutcome.WrongTurn, board.TryPlace(7, 7, Stone.White));
    }

    [Fact]
    public void Forfeit_SetsOpponentWinner()
    {
        var board = new OmokBoard();
        board.TryPlace(7, 7, Stone.Black);

        // 흑이 기권 -> 승자는 백
        board.Forfeit(Stone.Black);
        Assert.True(board.IsOver);
        Assert.Equal(Stone.White, board.Winner);
    }
}
