using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using OmokLan.Engine.Core;

namespace OmokLan.Engine.Play;

/// <summary>
/// CLI 터미널 환경을 위한 오목판 그리드 및 텍스트 렌더링 엔진.
/// 터미널의 표시 셀 수에 맞춰 텍스트를 구성합니다.
/// </summary>
public static class CliRenderer
{
    public const int BoardWidth = 38;
    public const int FrameHeight = 23;
    public const int MinimumFrameWidth = 42;
    public static readonly string[] ColLabels = ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O"];

    /// <summary>문자가 콘솔에서 전각(2칸)을 차지하는지 판별합니다.</summary>
    public static bool IsWideChar(char c)
    {
        // 한글 완성형 및 자모
        if (c >= 0xac00 && c <= 0xd7a3) return true;
        if (c >= 0x1100 && c <= 0x11ff) return true;
        if (c >= 0x3130 && c <= 0x318f) return true;

        // CJK 표의문자(한자)
        if (c >= 0x4e00 && c <= 0x9fff) return true;
        if (c >= 0x3400 && c <= 0x4dbf) return true;

        // CJK 기호 및 전각 문자
        if (c >= 0x3000 && c <= 0x303f) return true;
        if (c >= 0xff01 && c <= 0xff60) return true;
        if (c >= 0xffe0 && c <= 0xffe6) return true;
        if (c >= 0x3040 && c <= 0x30ff) return true;
        if (c >= 0x31f0 && c <= 0x31ff) return true;
        if (c >= 0xf900 && c <= 0xfaff) return true;

        return false;
    }

    /// <summary>한글(전각 2칸) 및 영문(반각 1칸)을 고려한 문자열의 실제 콘솔 표시 너비를 계산합니다.</summary>
    public static int GetDisplayWidth(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        int width = 0;
        foreach (char c in SanitizeDisplay(s))
        {
            width += IsWideChar(c) ? 2 : 1;
        }
        return width;
    }

    /// <summary>문자열을 목표 표시 너비(targetWidth)에 맞게 자르거나 공백을 채워 정렬합니다.</summary>
    public static string PadOrTrimDisplay(string s, int targetWidth)
    {
        if (targetWidth <= 0) return "";
        s = SanitizeDisplay(s);
        bool truncated = GetDisplayWidth(s) > targetWidth;
        int contentWidth = truncated && targetWidth >= 2 ? targetWidth - 2 : targetWidth;

        int currentWidth = 0;
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            int charWidth = IsWideChar(c) ? 2 : 1;
            if (currentWidth + charWidth > contentWidth) break;
            sb.Append(c);
            currentWidth += charWidth;
        }

        if (truncated && targetWidth >= 2)
        {
            sb.Append("..");
            currentWidth += 2;
        }
        if (currentWidth < targetWidth)
        {
            sb.Append(new string(' ', targetWidth - currentWidth));
        }
        return sb.ToString();
    }

    // 로그나 닉네임의 개행/탭/ESC가 커서를 이동시키지 않도록 합니다.
    public static string SanitizeDisplay(string? text)
    {
        var result = new StringBuilder();
        foreach (char c in text ?? "")
        {
            if (char.IsSurrogate(c)) continue;
            var category = char.GetUnicodeCategory(c);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format) continue;
            // 원/별/그리스 문자 등은 동아시아 콘솔과 폰트에 따라 1칸 또는 2칸입니다.
            result.Append(char.IsControl(c) ? ' ' : c > 0x7e && !IsWideChar(c) ? '?' : c);
        }
        return result.ToString();
    }

    public static string InputTail(string input, int width)
    {
        string text = SanitizeDisplay(input);
        int remaining = GetDisplayWidth(text);
        int start = 0;
        while (start < text.Length && remaining > Math.Max(0, width))
            remaining -= IsWideChar(text[start++]) ? 2 : 1;
        return text[start..];
    }

    public static IReadOnlyList<string> WrapDisplay(string text, int width)
    {
        // 한글 한 글자(2칸)도 온전히 표시할 수 있어야 합니다.
        if (width < 2) throw new ArgumentOutOfRangeException(nameof(width));
        var lines = new List<string>();
        var line = new StringBuilder();
        int cells = 0;
        foreach (char c in SanitizeDisplay(text))
        {
            int charWidth = IsWideChar(c) ? 2 : 1;
            if (cells + charWidth > width)
            {
                lines.Add(line.ToString());
                line.Clear();
                cells = 0;
            }
            line.Append(c);
            cells += charWidth;
        }
        if (line.Length > 0 || lines.Count == 0) lines.Add(line.ToString());
        return lines;
    }

    public static string FormatBoardPanel(int y, Stone[,] grid, (int X, int Y)? lastMove, string log, int width)
    {
        if (width < MinimumFrameWidth) throw new ArgumentOutOfRangeException(nameof(width));
        return FormatBoardRow(y, grid, lastMove) + " " + PadOrTrimDisplay(log, width - BoardWidth - 2) + "|";
    }

    /// <summary>다양한 입력 형식(단순 좌표, git 커밋, kubectl 등)에서 바둑판 좌표(X, Y)를 추출합니다.</summary>
    public static bool TryParseMove(string input, out int x, out int y)
    {
        x = -1;
        y = -1;

        if (string.IsNullOrWhiteSpace(input)) return false;

        var match = Regex.Match(input, @"([a-oA-O])\s*([0-9]{1,2})", RegexOptions.IgnoreCase);
        if (!match.Success) return false;

        string colStr = match.Groups[1].Value.ToUpperInvariant();
        if (!int.TryParse(match.Groups[2].Value, out int rowNum)) return false;

        int colIdx = Array.IndexOf(ColLabels, colStr);
        int rowIdx = rowNum - 1;

        if (colIdx >= 0 && colIdx < OmokBoard.Size && rowIdx >= 0 && rowIdx < OmokBoard.Size)
        {
            x = colIdx;
            y = rowIdx;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 오목판 1개 행(Row)을 터미널 텍스트로 빌드합니다.
    /// ASCII 기호로 동아시아 콘솔에서 폭이 달라지는 원/테두리 문자를 피합니다.
    /// 너비: 행 번호 5칸 + 15셀*2칸 + 여백/테두리 3칸 = 38칸.
    /// </summary>
    public static string FormatBoardRow(int y, Stone[,] grid, (int X, int Y)? lastMove)
    {
        var sb = new StringBuilder();
        sb.Append($"| {(y + 1),2} "); // 5칸

        for (int x = 0; x < OmokBoard.Size; x++)
        {
            var st = grid[x, y];
            if (st == Stone.Black)
            {
                sb.Append("X ");
            }
            else if (st == Stone.White)
            {
                sb.Append("O ");
            }
            else
            {
                bool isStar = (x == 3 || x == 7 || x == 11) && (y == 3 || y == 7 || y == 11);
                sb.Append(isStar ? "+ " : ". "); // 화점(+) 또는 마침표(.) = 2칸
            }
        }

        sb.Append("  |"); // 3칸
        return sb.ToString();
    }
}
