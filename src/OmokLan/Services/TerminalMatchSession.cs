using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using OmokLan.Engine.Core;
using OmokLan.Engine.Play;
using OmokLan.Mvvm;

namespace OmokLan.Services;

/// <summary>
/// 게임 시작 시 실제 Windows 터미널(콘솔)을 띄워 플레이하는 개발자 위장(스텔스) 세션.
/// 무깜빡임(In-place 커서 제어) 및 사용자 입력 버퍼 보존 설계.
/// </summary>
public sealed class TerminalMatchSession : IDisposable
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    private const uint WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;

    private static readonly string[] ColLabels = ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O"];

    private readonly OmokMatch _match;
    private readonly Action _onSessionEnded;
    private readonly object _consoleLock = new();

    private CancellationTokenSource? _sessionCts;
    private bool _isDisposed;
    private bool _isPanicMode;
    private bool _isInitialDrawn;
    private (int Left, int Top, int Width, int Height) _lastViewport;
    private readonly List<string> _recentLogs = new();
    private int _logScrollOffset;
    private readonly StringBuilder _inputBuffer = new();

    public TerminalMatchSession(OmokMatch match, Action onSessionEnded)
    {
        _match = match;
        _onSessionEnded = onSessionEnded;
    }

    /// <summary>콘솔 세션을 시작하고 WPF 메인 창을 최소화합니다.</summary>
    public void Start()
    {
        _sessionCts = new CancellationTokenSource();

        // 1. Windows 콘솔 할당
        AllocConsole();
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        
        // 🔒 완벽한 위장: 타이틀바를 순수 Windows PowerShell로 설정
        Console.Title = "Windows PowerShell";

        // 🔒 타이틀바 좌측 및 작업표시줄 아이콘을 실제 Windows PowerShell의 아이콘으로 교체
        ApplyPowerShellIconToConsole();

        try
        {
            // 초기 크기만 제안합니다. 렌더링은 실제 뷰포트 크기를 따릅니다.
            if (Console.BufferWidth < 120) Console.BufferWidth = 120;
            if (Console.BufferHeight < 35) Console.BufferHeight = 35;
            if (Console.WindowWidth < 102) Console.WindowWidth = 102;
            if (Console.WindowHeight < 28) Console.WindowHeight = 28;
        }
        catch
        {
            // 콘솔 창 크기 조절 예외는 무시
        }

        // 2. WPF 메인 윈도우 최소화
        Ui.Post(() =>
        {
            if (Application.Current?.MainWindow is { } mainWin)
            {
                mainWin.WindowState = WindowState.Minimized;
            }
        });

        // 3. 이벤트 바인딩
        _match.StateChanged += OnMatchStateChanged;
        _match.ChatReceived += OnMatchChatReceived;
        _match.Finished += OnMatchFinished;
        _match.Disconnected += OnMatchDisconnected;

        AddLog("INFO", "PowerShell Daemon host session initialized.");
        AddLog("INFO", $"Peer node connected -> [{_match.OpponentNick}]. Waiting for sync token...");

        // 4. 입력 처리 백그라운드 루프 시작 (논블로킹 Key 루프)
        var token = _sessionCts.Token;
        _ = Task.Run(() => RunCliInputLoopAsync(token));

        // 게임 개시
        _match.Begin();
        Redraw();
    }

    /// <summary>콘솔 창의 아이콘을 시스템의 실제 PowerShell/cmd 아이콘으로 교체하여 오목 아이콘을 숨깁니다.</summary>
    private static void ApplyPowerShellIconToConsole()
    {
        try
        {
            IntPtr hConsole = GetConsoleWindow();
            if (hConsole == IntPtr.Zero) return;

            string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string psExe = Path.Combine(sysDir, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(psExe))
            {
                psExe = Path.Combine(sysDir, "cmd.exe");
            }

            IntPtr hIcon = ExtractIcon(IntPtr.Zero, psExe, 0);
            if (hIcon != IntPtr.Zero)
            {
                SendMessage(hConsole, WM_SETICON, (IntPtr)ICON_SMALL, hIcon);
                SendMessage(hConsole, WM_SETICON, (IntPtr)ICON_BIG, hIcon);
            }
        }
        catch
        {
            // 아이콘 교체 실패는 무시
        }
    }

    private void OnMatchStateChanged()
    {
        if (_isPanicMode) return;
        Redraw();
    }

    private void OnMatchChatReceived(ChatEntry chat)
    {
        AddLog("CHAT", $"<{chat.Sender}> \"{chat.Text}\"");
        if (!_isPanicMode) Redraw();
    }

    private void OnMatchFinished(MatchResult result)
    {
        string endDesc = result.Winner == Stone.None
            ? "Sync completed (Equal)."
            : (result.Winner == _match.MyColor ? "🏆 Transaction committed (Local victory)." : "💔 Transaction rolled back (Defeat).");

        AddLog("RESULT", $"{endDesc} [Reason: {result.Reason}]");
        Redraw();
    }

    private void OnMatchDisconnected(string reason)
    {
        AddLog("WARN", $"Connection closed by peer node: {reason}");
        if (!_isPanicMode) Redraw();
    }

    private void AddLog(string level, string message)
    {
        lock (_consoleLock)
        {
            string time = DateTime.Now.ToString("HH:mm:ss.fff");
            string entry = $"[{time}] [{level}] {message}";
            _recentLogs.Add(entry);
            _logScrollOffset = 0;
            // 줄바꿈 전 메시지를 보관하여 창 크기 변경 시 다시 배치합니다.
            if (_recentLogs.Count > 14)
            {
                _recentLogs.RemoveAt(0);
            }
        }
    }

    public static bool TryParseMove(string input, out int x, out int y) => CliRenderer.TryParseMove(input, out x, out y);

    private static (int Left, int Top, int Width, int Height) GetViewport() =>
        (Console.WindowLeft, Console.WindowTop, Console.WindowWidth, Console.WindowHeight);

    /// <summary>각 행을 뷰포트의 절대 좌표에 덮어써 자동 줄바꿈과 스크롤을 방지합니다.</summary>
    private void Redraw()
    {
        lock (_consoleLock)
        {
            if (_isPanicMode || _isDisposed) return;

            try
            {
                Console.CursorVisible = false;
                var viewport = GetViewport();
                // 마지막 열은 자동 줄바꿈 방지, 프롬프트 뒤 한 칸은 커서 자리입니다.
                int width = Math.Min(98, viewport.Width - 1);
                if (width < 1 || viewport.Height < 2) return;
                if (!_isInitialDrawn || viewport != _lastViewport)
                {
                    Console.Clear();
                    viewport = GetViewport();
                    _lastViewport = viewport;
                    _isInitialDrawn = true;
                }

                void WriteAt(int x, int y, string text, int cells, ConsoleColor color)
                {
                    Console.SetCursorPosition(viewport.Left + x, viewport.Top + y);
                    Console.ForegroundColor = color;
                    Console.Write(CliRenderer.PadOrTrimDisplay(text, cells));
                }

                bool fits = width >= CliRenderer.MinimumFrameWidth && viewport.Height >= CliRenderer.FrameHeight;
                int promptRow = fits ? 22 : viewport.Height - 1;
                if (fits)
                {
                    var snap = _match.Snapshot();
                    string border = "+" + new string('-', CliRenderer.BoardWidth - 2) + "+" + new string('-', width - CliRenderer.BoardWidth - 1) + "+";
                    WriteAt(0, 0, border, width, ConsoleColor.Cyan);
                    string myColor = _match.MyColor == Stone.Black ? "X P1 (Master)" : (_match.MyColor == Stone.White ? "O P2 (Worker)" : "Syncing...");
                    string turn = _match.IsMyTurn ? $"* Local Lock ({snap.TurnSecondsRemaining,2}s)" : $"Peer Lock ({snap.TurnSecondsRemaining,2}s)";
                    string header = $"[K8S MESH] Peer: {_match.OpponentNick} | Tag: {myColor} | Status: {turn}";
                    WriteAt(0, 1, "|" + CliRenderer.PadOrTrimDisplay(header, width - 2) + "|", width, ConsoleColor.White);
                    WriteAt(0, 2, border, width, ConsoleColor.Cyan);
                    string labels = "|    " + string.Join(" ", CliRenderer.ColLabels) + "   |";
                    int logWidth = width - CliRenderer.BoardWidth - 2;
                    WriteAt(0, 3, labels + " " + CliRenderer.PadOrTrimDisplay("LIVE LOG STREAM & SYSTEM STATUS", logWidth) + "|", width, ConsoleColor.DarkGreen);
                    WriteAt(0, 4, "|" + new string(' ', 36) + "|" + new string(' ', width - 39) + "|", width, ConsoleColor.DarkGray);

                    var logLines = _recentLogs.SelectMany(entry =>
                        CliRenderer.WrapDisplay(entry, logWidth).Select(line => (Text: line, Entry: entry))).ToList();
                    _logScrollOffset = Math.Clamp(_logScrollOffset, 0, Math.Max(0, logLines.Count - OmokBoard.Size));
                    int firstLogLine = logLines.Count - OmokBoard.Size - _logScrollOffset;

                    for (int y = 0; y < OmokBoard.Size; y++)
                    {
                        int logIndex = firstLogLine + y;
                        string log = logIndex >= 0 ? logLines[logIndex].Text : "";
                        string entry = logIndex >= 0 ? logLines[logIndex].Entry : "";
                        string row = CliRenderer.FormatBoardPanel(y, snap.Grid, snap.LastMove, log, width);
                        WriteAt(0, y + 5, row, width, ConsoleColor.DarkGray);
                        for (int x = 0; x < OmokBoard.Size; x++)
                        {
                            var stone = snap.Grid[x, y];
                            bool last = snap.LastMove == (x, y);
                            ConsoleColor color = stone == Stone.None ? ConsoleColor.DarkCyan :
                                last ? ConsoleColor.Yellow : stone == Stone.Black ? ConsoleColor.Cyan : ConsoleColor.Red;
                            WriteAt(5 + x * 2, y + 5, row.Substring(5 + x * 2, 1), 1, color);
                        }
                        ConsoleColor logColor = entry.Contains("[INFO]") ? ConsoleColor.DarkGreen :
                            entry.Contains("[CHAT]") ? ConsoleColor.Magenta :
                            entry.Contains("[WARN]") ? ConsoleColor.Yellow :
                            entry.Contains("[RESULT]") ? ConsoleColor.Green : ConsoleColor.Gray;
                        WriteAt(39, y + 5, log, logWidth, logColor);
                    }
                    WriteAt(0, 20, border, width, ConsoleColor.Cyan);
                    string logPosition = _logScrollOffset == 0 ? "최신" : $"이전 {_logScrollOffset}줄";
                    WriteAt(0, 21, $" PgUp/PgDn 로그({logPosition}) | H8 | chat <내용> | boss | gui | resign | X=흑 O=백", width, ConsoleColor.DarkGray);
                }
                else
                {
                    WriteAt(0, 0, "Resize terminal to at least 43 columns x 23 rows.", width, ConsoleColor.Yellow);
                }

                string prefix = CliRenderer.PadOrTrimDisplay(" dev@prod-k8s:~$ ", Math.Min(16, width - 1));
                string input = CliRenderer.InputTail(_inputBuffer.ToString(), width - prefix.Length - 1);
                WriteAt(0, promptRow, prefix + input, width, ConsoleColor.Green);
                Console.SetCursorPosition(viewport.Left + prefix.Length + CliRenderer.GetDisplayWidth(input), viewport.Top + promptRow);
            }
            catch (IOException)
            {
                _isInitialDrawn = false;
            }
            catch (ArgumentOutOfRangeException)
            {
                // 창 크기가 프레임 출력 도중 바뀌었으면 다음 입력/타이머에서 다시 그립니다.
                _isInitialDrawn = false;
            }
            finally
            {
                if (!_isDisposed)
                {
                    try { Console.ResetColor(); Console.CursorVisible = true; }
                    catch (IOException) { }
                }
            }
        }
    }

    /// <summary>사용자 키보드 입력 수신 루프 (ReadKey 기반 대화형 논블로킹 입력)</summary>
    private async Task RunCliInputLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !_isDisposed)
        {
            bool keyAvailable;
            lock (_consoleLock)
            {
                if (_isDisposed) return;
                keyAvailable = Console.KeyAvailable;
            }
            if (!keyAvailable)
            {
                try { await Task.Delay(25, ct); }
                catch (OperationCanceledException) { return; }
                lock (_consoleLock)
                {
                    if (_isDisposed) return;
                    if (!_isPanicMode && (!_isInitialDrawn || GetViewport() != _lastViewport)) Redraw();
                }
                continue;
            }

            ConsoleKeyInfo keyInfo;
            lock (_consoleLock)
            {
                if (_isDisposed) return;
                if (!Console.KeyAvailable) continue;
                keyInfo = Console.ReadKey(intercept: true);
            }

            if (_isPanicMode)
            {
                // 보스 패닉 화면에서 아무 키나 누르면 오목 화면으로 복구
                _isPanicMode = false;
                _isInitialDrawn = false;
                Redraw();
                continue;
            }

            if (keyInfo.Key == ConsoleKey.Escape)
            {
                // ESC 키 누르면 즉각 보스 패닉 화면
                TriggerPanicScreen();
                continue;
            }

            if (keyInfo.Key is ConsoleKey.PageUp or ConsoleKey.PageDown)
            {
                lock (_consoleLock)
                {
                    _logScrollOffset = Math.Max(0, _logScrollOffset + (keyInfo.Key == ConsoleKey.PageUp ? OmokBoard.Size : -OmokBoard.Size));
                }
                Redraw();
                continue;
            }

            if (keyInfo.Key == ConsoleKey.Enter)
            {
                string command;
                lock (_consoleLock)
                {
                    command = _inputBuffer.ToString().Trim();
                    _inputBuffer.Clear();
                }

                if (!string.IsNullOrEmpty(command))
                {
                    await ProcessCommandAsync(command);
                }
                else
                {
                    // 게임이 끝난 상태에서 엔터를 치면 GUI 로비로 복귀
                    if (_match.Snapshot().IsOver)
                    {
                        RestoreGui();
                        return;
                    }
                }

                Redraw();
                continue;
            }

            if (keyInfo.Key == ConsoleKey.Backspace)
            {
                lock (_consoleLock)
                {
                    if (_inputBuffer.Length > 0)
                    {
                        _inputBuffer.Remove(_inputBuffer.Length - 1, 1);
                    }
                }
                Redraw();
                continue;
            }

            // 일반 텍스트 문자 입력
            if (!char.IsControl(keyInfo.KeyChar))
            {
                lock (_consoleLock)
                {
                    _inputBuffer.Append(keyInfo.KeyChar);
                }
                Redraw();
            }
        }
    }

    /// <summary>명령어 처리</summary>
    private async Task ProcessCommandAsync(string input)
    {
        string lower = input.ToLowerInvariant();

        // 1. 긴급 보스 키 (boss 또는 panic)
        if (lower == "boss" || lower == "panic")
        {
            TriggerPanicScreen();
            return;
        }

        // 2. GUI 복귀 명령어
        if (lower == "gui" || lower == "window")
        {
            RestoreGui();
            return;
        }

        // 3. 기권 명령어
        if (lower == "resign" || lower == "quit" || lower == "exit")
        {
            AddLog("WARN", "기권을 선언합니다...");
            await _match.ResignAsync();
            return;
        }

        // 4. 일시정지 (AI 모드)
        if (lower == "pause" || lower == "resume")
        {
            if (_match.IsPaused)
            {
                _match.Resume();
                AddLog("INFO", "AI 대결이 재개되었습니다.");
            }
            else
            {
                _match.Pause();
                AddLog("INFO", "AI 대결이 일시정지되었습니다.");
            }
            return;
        }

        // 5. 채팅 명령어: chat 안녕하세요
        if (lower.StartsWith("chat "))
        {
            string msg = input.Substring(5).Trim();
            if (!string.IsNullOrEmpty(msg))
            {
                await _match.SendChatAsync(msg);
                AddLog("CHAT", $"<나> \"{msg}\"");
            }
            return;
        }

        // 6. 착수 명령어 파싱 (H8, h8, h 8, git commit -m "H8", kubectl apply -f h8.yaml 등)
        if (TryParseMove(input, out int x, out int y))
        {
            if (!_match.IsMyTurn)
            {
                AddLog("WARN", "내 차례가 아닙니다. 상대방의 착수를 기다리십시오.");
                return;
            }

            bool ok = await _match.PlaceAsync(x, y);
            if (ok)
            {
                AddLog("MOVE", $"착수 완료: {ColLabels[x]}{y + 1} ({input})");
            }
            else
            {
                AddLog("WARN", $"착수 실패: {ColLabels[x]}{y + 1} 위치에 둘 수 없습니다.");
            }
            return;
        }

        // 알 수 없는 명령어
        AddLog("WARN", $"알 수 없는 명령: '{input}'. 예: 'H8', 'chat 안녕', 'boss'");
    }

    /// <summary>긴급 보스 키: 가짜 MSBuild / Docker 빌드 화면을 띄웁니다.</summary>
    private void TriggerPanicScreen()
    {
        lock (_consoleLock)
        {
            _isPanicMode = true;
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("MSBuild version 17.12.0 for .NET (x64)");
            Console.WriteLine("Building project 'C:\\Projects\\EnterpriseBackend\\Service.csproj' (default targets):");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("(CoreCompile target) ->");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("  Compiling 1,842 source files...");
            Console.WriteLine("  Optimizing assemblies... [============================>    ] 74%");
            Console.WriteLine("  17:05:12.102 [CSC] Compiling OmokLan.Core.dll (Tier-1 PGO enabled)");
            Console.WriteLine("  17:05:13.491 [DOCKER] Step 8/14 : COPY --from=build /app/publish .");
            Console.WriteLine("  17:05:15.882 [KUBE] Syncing deployment/omok-core to cluster prod-k8s...");
            Console.WriteLine("  17:05:16.012 [INFO] Health probe 200 OK on port 8080. Pod 1 of 6 ready.");
            Console.WriteLine("  17:05:17.451 [INFO] Database migration applied successfully: V20261022__add_index.sql");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(" [System Busy] 진행 중입니다... (ESC 또는 아무 키를 누르면 원래 콘솔로 복귀)");
        }
    }

    /// <summary>콘솔 세션을 종료하고 원래 WPF 메인 화면으로 복원합니다.</summary>
    private void RestoreGui()
    {
        Dispose();
        Ui.Post(() =>
        {
            if (Application.Current?.MainWindow is { } mainWin)
            {
                mainWin.WindowState = WindowState.Normal;
                mainWin.Activate();
            }
            _onSessionEnded();
        });
    }

    public void Dispose()
    {
        lock (_consoleLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _sessionCts?.Cancel();
            _sessionCts?.Dispose();

            _match.StateChanged -= OnMatchStateChanged;
            _match.ChatReceived -= OnMatchChatReceived;
            _match.Finished -= OnMatchFinished;
            _match.Disconnected -= OnMatchDisconnected;

            FreeConsole();
        }
    }
}
