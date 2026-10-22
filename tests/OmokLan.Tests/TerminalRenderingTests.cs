using System.Text;
using OmokLan.Engine.Core;
using OmokLan.Engine.Play;
using Xunit;

namespace OmokLan.Tests;

public class TerminalRenderingTests
{
    [Fact]
    public void Board_Uses_Only_Unambiguous_Ascii_Cells()
    {
        var grid = new Stone[15, 15];
        grid[5, 3] = Stone.Black;
        grid[6, 3] = Stone.White;
        string row = CliRenderer.FormatBoardRow(3, grid, (6, 3));
        Assert.Equal('X', row[15]);
        Assert.Equal('O', row[17]);
        Assert.Equal('|', row[37]);
        Assert.All(row, c => Assert.InRange((int)c, 32, 126));
    }

    [Theory]
    [InlineData("abc", 3, "abc")]
    [InlineData("한글", 4, "한글")]
    [InlineData("abcd", 3, "a..")]
    [InlineData("한글문", 5, "한.. ")]
    [InlineData("abcd", 1, "a")]
    [InlineData("한글", 1, " ")]
    [InlineData("abc", 0, "")]
    public void Clipping_Preserves_Exact_Fits_And_Never_Exceeds_Width(string text, int width, string expected)
    {
        string result = CliRenderer.PadOrTrimDisplay(text, width);
        Assert.Equal(expected, result);
        Assert.Equal(width, CliRenderer.GetDisplayWidth(result));
    }

    [Fact]
    public void Logs_Cannot_Inject_Cursor_Movement_Or_Line_Breaks()
    {
        string result = CliRenderer.PadOrTrimDisplay("닉\r\n채팅\t\u001b[2J\b😊e\u0301\u200d", 40);
        Assert.DoesNotContain(result, char.IsControl);
        Assert.DoesNotContain(result, char.IsSurrogate);
        Assert.DoesNotContain('\u0301', result);
        Assert.DoesNotContain('\u200d', result);
        Assert.Equal(40, CliRenderer.GetDisplayWidth(result));
    }

    [Fact]
    public void Ambiguous_Width_Symbols_In_Peer_Text_Are_Normalized()
    {
        Assert.Equal("한글 ?????", CliRenderer.SanitizeDisplay("한글 ●○★│Ω"));
        Assert.Equal(10, CliRenderer.GetDisplayWidth("한글 ●○★│Ω"));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(39)]
    [InlineData(58)]
    public void Wrapped_Chat_Preserves_All_Text_And_Fits_Panel(int width)
    {
        string chat = "[18:27:21.557] [CHAT] <상대방> \"" + new string('가', 200) + " English 끝\"";
        var lines = CliRenderer.WrapDisplay(chat, width);
        Assert.Equal(chat, string.Concat(lines));
        Assert.All(lines, line => Assert.InRange(CliRenderer.GetDisplayWidth(line), 1, width));
        Assert.DoesNotContain(lines, line => line.Contains(".."));
        var grid = new Stone[15, 15];
        foreach (string line in lines)
        {
            string row = CliRenderer.FormatBoardPanel(0, grid, null, line, width + 40);
            Assert.Equal(width + 40, CliRenderer.GetDisplayWidth(row));
            Assert.EndsWith("|", row);
        }
    }

    [Theory]
    [InlineData("abcd", 2, 2)]
    [InlineData("한글", 2, 2)]
    [InlineData("", 2, 1)]
    public void Wrapping_Exact_Fits_Does_Not_Add_An_Extra_Line(string text, int width, int count)
    {
        var lines = CliRenderer.WrapDisplay(text, width);
        Assert.Equal(count, lines.Count);
        Assert.Equal(text, string.Concat(lines));
    }

    [Theory]
    [InlineData("chat 안녕하세요", 7, "하세요")]
    [InlineData("chat abc", 3, "abc")]
    [InlineData("한글", 1, "")]
    [InlineData("abc", 0, "")]
    public void Input_Tail_Leaves_Cursor_Inside_Viewport(string input, int width, string expected)
    {
        string tail = CliRenderer.InputTail(input, width);
        Assert.Equal(expected, tail);
        Assert.True(CliRenderer.GetDisplayWidth(tail) <= width);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(79)]
    [InlineData(98)]
    public void Live_Board_And_Log_Panel_Keep_Borders_Aligned(int width)
    {
        var grid = new Stone[15, 15];
        for (int y = 0; y < 15; y++)
        {
            for (int x = 0; x < 15; x++) grid[x, y] = x % 2 == 0 ? Stone.Black : Stone.White;
            foreach (string log in new[] { "", "[WARN] F5 위치에 둘 수 없습니다.", new string('가', 200), "chat\r\n\t\u001b[2J" })
            {
                string row = CliRenderer.FormatBoardPanel(y, grid, (5, 4), log, width);
                Assert.Equal(width, CliRenderer.GetDisplayWidth(row));
                Assert.Equal('|', row[37]);
                Assert.EndsWith("|", row);
                Assert.DoesNotContain(row, char.IsControl);
            }
        }
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
