using System.Text;
using OmokLan.Engine.Core;
using OmokLan.Engine.Play;
using Xunit;

namespace OmokLan.Tests;

public class TerminalSimulationQaTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(40)]
    public void Simulated_Match_Should_Never_Break_Board_Width(int moveCount)
    {
        var board = new OmokBoard();
        var rng = new Random(42);

        // moveCount번만큼 무작위 유효 착수 시뮬레이션
        int placed = 0;
        for (int i = 0; i < moveCount && !board.IsOver; i++)
        {
            int x = rng.Next(OmokBoard.Size);
            int y = rng.Next(OmokBoard.Size);
            if (board.TryPlace(x, y, board.Turn).IsAccepted())
            {
                placed++;
            }
        }

        // 전체 15개 행에 대해 렌더링 너비가 정확히 38칸인지 전수 검사
        var grid = board.CopyGrid();
        for (int y = 0; y < OmokBoard.Size; y++)
        {
            string rowText = CliRenderer.FormatBoardRow(y, grid, board.LastMove);
            int width = CliRenderer.GetDisplayWidth(rowText);
            Assert.True(width == 38, $"Row {y + 1} display width was {width}, expected 38 (Placed moves: {placed})");
        }
    }

    [Fact]
    public void Massive_Chat_Messages_Should_Not_Crash_Or_Distort_Width()
    {
        string[] sampleChats =
        [
            "안녕하세요",
            "F4 착수합니다",
            "거기 두시면 제가 이깁니다 ㅎㅎ",
            "Long message: This is an enterprise deployment monitoring channel for cluster 01.",
            "한글과 English 와 숫자가 12345 섞인 특수문자 !@#$%^&*() 메시지",
            "완전 긴 메시지: 가나다라마바사아자차카타파하가나다라마바사아자차카타파하가나다라마바사아자차카타파하",
            "짧음",
            "😊 이모지는 제외되거나 안전하게 처리됨"
        ];

        foreach (var chat in sampleChats)
        {
            string padded = CliRenderer.PadOrTrimDisplay(chat, 60);
            int w = CliRenderer.GetDisplayWidth(padded);
            Assert.True(w == 60, $"Chat '{chat}' padded width was {w}, expected exactly 60");
        }
    }
}
