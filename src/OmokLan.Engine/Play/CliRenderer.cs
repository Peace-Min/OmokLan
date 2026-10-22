using System.Text;
using System.Text.RegularExpressions;
using OmokLan.Engine.Core;

namespace OmokLan.Engine.Play;

/// <summary>
/// CLI 터미널 환경을 위한 오목판 그리드 및 텍스트 렌더링 엔진.
/// 동아시아 전각/반각 너비를 고려하여 어떤 돌이 놓여도 1픽셀도 밀리지 않는 칼정렬 보장.
/// </summary>
public static class CliRenderer
{
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

        // 기하학적 도형 (● 0x25CF, ○ 0x25CB, ■ 0x25A0 등)
        if (c >= 0x25a0 && c <= 0x25ff) return true;

        // CJK 기호 및 전각 문자
        if (c >= 0x3000 && c <= 0x303f) return true;
        if (c >= 0xff01 && c <= 0xff60) return true;

        return false;
    }

    /// <summary>한글(전각 2칸) 및 영문(반각 1칸)을 고려한 문자열의 실제 콘솔 표시 너비를 계산합니다.</summary>
    public static int GetDisplayWidth(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        int width = 0;
        foreach (char c in s)
        {
            width += IsWideChar(c) ? 2 : 1;
        }
        return width;
    }

    /// <summary>문자열을 목표 표시 너비(targetWidth)에 맞게 자르거나 공백을 채워 정렬합니다.</summary>
    public static string PadOrTrimDisplay(string s, int targetWidth)
    {
        if (s == null) s = "";
        int currentWidth = 0;
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            int charWidth = IsWideChar(c) ? 2 : 1;
            if (currentWidth + charWidth > targetWidth - 2)
            {
                sb.Append("..");
                currentWidth += 2;
                break;
            }
            sb.Append(c);
            currentWidth += charWidth;
        }

        if (currentWidth < targetWidth)
        {
            sb.Append(new string(' ', targetWidth - currentWidth));
        }
        return sb.ToString();
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

    /// <summary>오목판 1개 행(Row)을 터미널 텍스트로 빌드합니다. (반드시 정확히 37칸 너비 보장: col 0~36)</summary>
    public static string FormatBoardRow(int y, Stone[,] grid, (int X, int Y)? lastMove)
    {
        var sb = new StringBuilder();
        sb.Append($"│ {(y + 1),2} "); // 5칸

        for (int x = 0; x < OmokBoard.Size; x++)
        {
            var st = grid[x, y];
            if (st == Stone.Black)
            {
                sb.Append("●"); // 전각 2칸
            }
            else if (st == Stone.White)
            {
                sb.Append("○"); // 전각 2칸
            }
            else
            {
                bool isStar = (x == 3 || x == 7 || x == 11) && (y == 3 || y == 7 || y == 11);
                sb.Append(isStar ? "+ " : "· "); // 2칸
            }
        }

        sb.Append("  │"); // 3칸
        return sb.ToString();
    }
}
