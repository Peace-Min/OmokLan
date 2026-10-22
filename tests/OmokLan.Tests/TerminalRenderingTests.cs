using System.Text;
using OmokLan.Engine.Core;
using OmokLan.Engine.Play;
using Xunit;

namespace OmokLan.Tests;

public class TerminalRenderingTests
{
    [Fact]
    public void DisplayWidth_Of_Stones_Should_Equal_Exactly_Two()
    {
        // 흑돌 1칸 + 공백 1칸 = 2칸
        Assert.Equal(2, CliRenderer.GetDisplayWidth("● "));
        // 백돌 1칸 + 공백 1칸 = 2칸
        Assert.Equal(2, CliRenderer.GetDisplayWidth("○ "));
        // 빈자리 1칸 + 공백 1칸 = 2칸
        Assert.Equal(2, CliRenderer.GetDisplayWidth(". "));
        // 화점 1칸 + 공백 1칸 = 2칸
        Assert.Equal(2, CliRenderer.GetDisplayWidth("+ "));
    }

    [Theory]
    [InlineData("H8", 7, 7)]
    [InlineData("h8", 7, 7)]
    [InlineData("F4", 5, 3)]
    [InlineData("f5", 5, 4)]
    [InlineData("A1", 0, 0)]
    [InlineData("O15", 14, 14)]
    [InlineData("git commit -m \"f4\"", 5, 3)]
    [InlineData("kubectl apply -f stone_h8.yaml", 7, 7)]
    [InlineData("curl -X POST /move/H8", 7, 7)]
    public void TryParseMove_Should_Parse_All_Valid_Coordinates(string input, int expectedX, int expectedY)
    {
        bool success = CliRenderer.TryParseMove(input, out int x, out int y);
        Assert.True(success);
        Assert.Equal(expectedX, x);
        Assert.Equal(expectedY, y);
    }

    [Fact]
    public void Board_Row_Width_Should_Remain_Exactly_38_Regardless_Of_Stone_Placements()
    {
        var grid = new Stone[15, 15];

        // 1. 완전히 비어있을 때 행 4(화점 D4 포함) 너비 검증
        string emptyRow = CliRenderer.FormatBoardRow(3, grid, null);
        Assert.Equal(38, CliRenderer.GetDisplayWidth(emptyRow));

        // 2. 사용자가 버그를 겪었던 F4(x=5, y=3)에 돌을 놓았을 때 너비 검증
        grid[5, 3] = Stone.Black;
        string withF4 = CliRenderer.FormatBoardRow(3, grid, (5, 3));
        Assert.Equal(38, CliRenderer.GetDisplayWidth(withF4));

        // 3. F5(x=5, y=4)에 상대방이 백돌을 놓았을 때 너비 검증
        grid[5, 4] = Stone.White;
        string withF5 = CliRenderer.FormatBoardRow(4, grid, (5, 4));
        Assert.Equal(38, CliRenderer.GetDisplayWidth(withF5));

        // 4. 같은 행에 돌이 10개 이상 빽빽하게 놓여도 너비 38칸 유지 검증
        for (int x = 0; x < 10; x++)
        {
            grid[x, 7] = (x % 2 == 0) ? Stone.Black : Stone.White;
        }
        string crowdedRow = CliRenderer.FormatBoardRow(7, grid, null);
        Assert.Equal(38, CliRenderer.GetDisplayWidth(crowdedRow));
    }

    [Fact]
    public void Korean_Chat_Padding_Should_Fit_Target_Width_Without_Shifting()
    {
        string shortMsg = "[CHAT] 안녕";
        string longMsg = "[CHAT] <상대방> \"오늘 날씨가 참 좋은데 한 판 재미있게 즐겨봅시다!\"";

        string paddedShort = CliRenderer.PadOrTrimDisplay(shortMsg, 60);
        string paddedLong = CliRenderer.PadOrTrimDisplay(longMsg, 60);

        Assert.Equal(60, CliRenderer.GetDisplayWidth(paddedShort));
        Assert.Equal(60, CliRenderer.GetDisplayWidth(paddedLong));
    }
}
