using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
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
    private readonly List<string> _recentLogs = new();
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
            if (Console.WindowWidth < 105) Console.WindowWidth = 105;
            if (Console.WindowHeight < 32) Console.WindowHeight = 32;
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
        Task.Run(() => RunCliInputLoopAsync(_sessionCts.Token));

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
            // 15줄 화면에 맞춰 최대 14줄까지 충분히 보관
            if (_recentLogs.Count > 14)
            {
                _recentLogs.RemoveAt(0);
            }
        }
    }

    private static int GetDisplayWidth(string s) => CliRenderer.GetDisplayWidth(s);
    private static string PadOrTrimDisplay(string s, int targetWidth) => CliRenderer.PadOrTrimDisplay(s, targetWidth);
    public static bool TryParseMove(string input, out int x, out int y) => CliRenderer.TryParseMove(input, out x, out y);

    /// <summary>
    /// 화면을 깜빡임 없이 제자리(In-place)에서 부드럽게 갱신합니다.
    /// 오목판 셀을 정확히 2칸(Width 2)으로 고정하여 어떤 위치에 착수해도 사각형이 깨지지 않습니다.
    /// </summary>
    private void Redraw()
    {
        lock (_consoleLock)
        {
            if (_isPanicMode || _isDisposed) return;

            try
            {
                Console.CursorVisible = false;

                // 최초 1회만 화면 초기화, 이후에는 제자리 덮어쓰기로 깜빡임 박멸
                if (!_isInitialDrawn)
                {
                    Console.Clear();
                    _isInitialDrawn = true;
                }
                else
                {
                    Console.SetCursorPosition(0, 0);
                }

                var snap = _match.Snapshot();

                // 1. 상단 터미널 헤더 (총 101칸 너비 칼일치)
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("┌────────────────────────────────────┬──────────────────────────────────────────────────────────────┐");
                Console.Write("│ ");
                Console.ForegroundColor = ConsoleColor.White;
                string myColorStr = _match.MyColor == Stone.Black ? "● P1 (Master)" : (_match.MyColor == Stone.White ? "○ P2 (Worker)" : "Syncing...");
                string turnStr = _match.IsMyTurn ? $"★ Local Lock ({snap.TurnSecondsRemaining,2}s)" : $"Peer Lock ({snap.TurnSecondsRemaining,2}s)";
                string headerInfo = $"[K8S MESH] Peer: {_match.OpponentNick}  |  Tag: {myColorStr}  |  Status: {turnStr}";
                Console.Write(PadOrTrimDisplay(headerInfo, 97));
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(" │");
                Console.WriteLine("├────────────────────────────────────┼──────────────────────────────────────────────────────────────┤");

                // 2. 15x15 오목판 매트릭스 렌더링 (좌측 37칸 + 우측 63칸 = 101칸)
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("│    A B C D E F G H I J K L M N O   │ ");
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.WriteLine(PadOrTrimDisplay("LIVE LOG STREAM & SYSTEM STATUS (RECENT CHAT & PACKETS)", 60) + " │");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("│                                    │                                                              │");

                for (int y = 0; y < OmokBoard.Size; y++)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($"│ {(y + 1),2} ");

                    for (int x = 0; x < OmokBoard.Size; x++)
                    {
                        var st = snap.Grid[x, y];
                        bool isLast = snap.LastMove.HasValue && snap.LastMove.Value.X == x && snap.LastMove.Value.Y == y;

                        // 🔒 핵심: 콘솔에서 모든 셀은 정확히 2칸 너비(Width 2)로 100% 통일
                        // ● 와 ○ 는 전각 문자 자체로 2칸이므로 뒤에 공백을 붙이지 않음.
                        // · 와 + 는 1칸 문자이므로 뒤에 공백 ' '을 붙여 2칸으로 통일.
                        if (st == Stone.Black)
                        {
                            Console.ForegroundColor = isLast ? ConsoleColor.Yellow : ConsoleColor.Cyan;
                            Console.Write("●"); // 전각 2칸
                        }
                        else if (st == Stone.White)
                        {
                            Console.ForegroundColor = isLast ? ConsoleColor.Yellow : ConsoleColor.Red;
                            Console.Write("○"); // 전각 2칸
                        }
                        else
                        {
                            // 화점
                            bool isStar = (x == 3 || x == 7 || x == 11) && (y == 3 || y == 7 || y == 11);
                            if (isStar)
                            {
                                Console.ForegroundColor = ConsoleColor.DarkCyan;
                                Console.Write("+ "); // 1 + 1 = 2칸
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.DarkGray;
                                Console.Write("· "); // 1 + 1 = 2칸
                            }
                        }
                    }

                    // 우측 대화/로그 스트림 (넉넉한 60칸 너비 제공)
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("  │ ");

                    int logIdx = y - (OmokBoard.Size - _recentLogs.Count);
                    if (logIdx >= 0 && logIdx < _recentLogs.Count)
                    {
                        string logLine = _recentLogs[logIdx];
                        if (logLine.Contains("[INFO]")) Console.ForegroundColor = ConsoleColor.DarkGreen;
                        else if (logLine.Contains("[CHAT]")) Console.ForegroundColor = ConsoleColor.Magenta;
                        else if (logLine.Contains("[WARN]")) Console.ForegroundColor = ConsoleColor.Yellow;
                        else if (logLine.Contains("[RESULT]")) Console.ForegroundColor = ConsoleColor.Green;
                        else Console.ForegroundColor = ConsoleColor.Gray;

                        // 60칸 너비에 맞춰서 출력 (한글 전각 폭 정확 계산)
                        string formattedLine = PadOrTrimDisplay(logLine, 60);
                        Console.Write(formattedLine);
                    }
                    else
                    {
                        Console.Write(new string(' ', 60));
                    }

                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine(" │");
                }

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("└────────────────────────────────────┴──────────────────────────────────────────────────────────────┘");
                Console.ResetColor();

                // 3. 안내 및 명령어 힌트
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(" [명령어 가이드] 좌표(H8) | git commit -m 'H8' | chat <내용> | boss(긴급위장) | gui | resign");
                Console.ResetColor();

                // 4. 하단 프롬프트 및 현재 입력 버퍼 재출력 (입력칸 완벽 보존)
                Console.ForegroundColor = ConsoleColor.Green;
                string promptPrefix = " dev@prod-k8s:~$ ";
                Console.Write(promptPrefix);
                Console.ResetColor();

                string currentBuffer = _inputBuffer.ToString();
                Console.Write(currentBuffer);

                // 이전 입력 잔상 지우기 위한 여백 채움
                int padSpaces = Math.Max(0, 40 - currentBuffer.Length);
                Console.Write(new string(' ', padSpaces));

                // 커서를 입력 버퍼 끝으로 재배치
                int targetCol = promptPrefix.Length + currentBuffer.Length;
                int targetRow = Console.CursorTop;
                Console.SetCursorPosition(targetCol, targetRow);
                Console.CursorVisible = true;
            }
            catch
            {
                // 콘솔 조작 실패 시 무시
            }
        }
    }

    /// <summary>사용자 키보드 입력 수신 루프 (ReadKey 기반 대화형 논블로킹 입력)</summary>
        private async Task RunCliInputLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !_isDisposed)
            {
                if (!Console.KeyAvailable)
                {
                    await Task.Delay(25, ct);
                    continue;
                }

                ConsoleKeyInfo keyInfo = Console.ReadKey(intercept: true);

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
