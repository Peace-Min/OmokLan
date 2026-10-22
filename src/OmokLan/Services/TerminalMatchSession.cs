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

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    private static readonly string[] ColLabels = ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O"];

    private readonly OmokMatch _match;
    private readonly Action _onSessionEnded;
    private readonly object _consoleLock = new();

    private CancellationTokenSource? _sessionCts;
    private bool _isDisposed;
    private bool _isPanicMode;
    private readonly List<string> _recentLogs = new();

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
        Console.Title = $"powershell.exe - omoklan@prod-k8s: ~/deploy (Duel vs {_match.OpponentNick})";

        try
        {
            if (Console.WindowWidth < 90) Console.WindowWidth = 95;
            if (Console.WindowHeight < 32) Console.WindowHeight = 35;
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

        AddLog("INFO", "OmokLan Terminal Engine v1.1 initialized.");
        AddLog("INFO", $"Connected to peer [{_match.OpponentNick}]. Waiting for coin toss...");

        // 4. 입력 처리 백그라운드 루프 시작
        Task.Run(() => RunCliLoopAsync(_sessionCts.Token));

        // 게임 개시
        _match.Begin();
        Redraw();
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
            ? "무승부로 경기가 종료되었습니다."
            : (result.Winner == _match.MyColor ? "🏆 경기에서 승리하셨습니다!" : "💔 경기에서 패배하셨습니다.");

        AddLog("RESULT", $"{endDesc} (사유: {result.Reason})");
        Redraw();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n [MATCH FINISHED] 경기가 완료되었습니다.");
        Console.WriteLine(" 로비로 복귀하려면 엔터(Enter) 키를 누르십시오...");
        Console.ResetColor();
    }

    private void OnMatchDisconnected(string reason)
    {
        AddLog("WARN", $"상대방과의 연결이 끊겼습니다: {reason}");
        if (!_isPanicMode) Redraw();
    }

    private void AddLog(string level, string message)
    {
        lock (_consoleLock)
        {
            string time = DateTime.Now.ToString("HH:mm:ss.fff");
            string entry = $"[{time}] [{level}] {message}";
            _recentLogs.Add(entry);
            if (_recentLogs.Count > 6)
            {
                _recentLogs.RemoveAt(0);
            }
        }
    }

    /// <summary>화면 전체를 깨끗하게 다시 그립니다.</summary>
    private void Redraw()
    {
        lock (_consoleLock)
        {
            if (_isPanicMode || _isDisposed) return;

            try
            {
                Console.Clear();
            }
            catch
            {
                // 콘솔 버퍼 초기화 실패 시 무시
            }

            var snap = _match.Snapshot();

            // 1. 상단 터미널 헤더
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("┌────────────────────────────────────────────────────────────────────────────────────────┐");
            Console.Write("│ ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"[K8S PROD NODE MESH] Match vs {_match.OpponentNick,-15}");
            Console.ForegroundColor = ConsoleColor.Yellow;
            string myColorStr = _match.MyColor == Stone.Black ? "● 흑 (선공)" : (_match.MyColor == Stone.White ? "○ 백 (후공)" : "결정 중");
            Console.Write($" | 돌: {myColorStr,-10}");
            Console.ForegroundColor = ConsoleColor.Green;
            string turnStr = _match.IsMyTurn ? "★ 내 차례 (30s) " : "상대방 턴 대기 ";
            Console.Write($" | 상태: {turnStr,-16}");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("│");
            Console.WriteLine("├────────────────────────────────────────────────────────────────────────────────────────┤");

            // 2. 15x15 오목판 매트릭스 렌더링
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("│    A  B  C  D  E  F  G  H  I  J  K  L  M  N  O   │   LIVE LOG STREAM & SYSTEM STATUS    │");
            Console.WriteLine("│                                                   │                                      │");

            for (int y = 0; y < OmokBoard.Size; y++)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"│ {(y + 1),2} ");

                for (int x = 0; x < OmokBoard.Size; x++)
                {
                    var st = snap.Grid[x, y];
                    bool isLast = snap.LastMove.HasValue && snap.LastMove.Value.X == x && snap.LastMove.Value.Y == y;

                    if (st == Stone.Black)
                    {
                        Console.ForegroundColor = isLast ? ConsoleColor.Yellow : ConsoleColor.Cyan;
                        Console.Write("● ");
                    }
                    else if (st == Stone.White)
                    {
                        Console.ForegroundColor = isLast ? ConsoleColor.Yellow : ConsoleColor.Red;
                        Console.Write("○ ");
                    }
                    else
                    {
                        // 화점 (3, 3), (3, 11), (7, 7), (11, 3), (11, 11)
                        bool isStar = (x == 3 || x == 7 || x == 11) && (y == 3 || y == 7 || y == 11);
                        if (isStar)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkCyan;
                            Console.Write("┼ ");
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.Write("· ");
                        }
                    }
                }

                // 우측 로그 스트림 매핑 (행 번호에 따라 최근 로그 출력)
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(" │ ");

                int logIdx = y - (OmokBoard.Size - _recentLogs.Count);
                if (logIdx >= 0 && logIdx < _recentLogs.Count)
                {
                    string logLine = _recentLogs[logIdx];
                    if (logLine.Contains("[INFO]")) Console.ForegroundColor = ConsoleColor.DarkGreen;
                    else if (logLine.Contains("[CHAT]")) Console.ForegroundColor = ConsoleColor.Magenta;
                    else if (logLine.Contains("[WARN]")) Console.ForegroundColor = ConsoleColor.Yellow;
                    else if (logLine.Contains("[RESULT]")) Console.ForegroundColor = ConsoleColor.Green;
                    else Console.ForegroundColor = ConsoleColor.Gray;

                    // 최대 36자 맞춤
                    if (logLine.Length > 36) logLine = logLine.Substring(0, 33) + "...";
                    Console.Write($"{logLine,-36}");
                }
                else
                {
                    Console.Write(new string(' ', 36));
                }

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(" │");
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("└────────────────────────────────────────────────────────────────────────────────────────┘");
            Console.ResetColor();

            // 3. 안내 및 명령어 힌트
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(" [명령어 가이드] 좌표(H8) | git commit -m 'H8' | chat <내용> | boss(긴급위장) | gui | resign");
            Console.ResetColor();

            // 프롬프트 출력
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write(" dev@prod-k8s:~$ ");
            Console.ResetColor();
        }
    }

    /// <summary>사용자 키보드 입력 수신 루프</summary>
        private async Task RunCliLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !_isDisposed)
            {
                string? input = await Task.Run(Console.ReadLine, ct);
                if (string.IsNullOrWhiteSpace(input)) continue;

                input = input.Trim();

                if (_isPanicMode)
                {
                    // 패닉 모드 중 아무 키나 입력하면 해제
                    _isPanicMode = false;
                    Redraw();
                    continue;
                }

                string lower = input.ToLowerInvariant();

                // 1. 긴급 보스 키 (boss 또는 panic)
                if (lower == "boss" || lower == "panic")
                {
                    TriggerPanicScreen();
                    continue;
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
                    continue;
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
                    Redraw();
                    continue;
                }

                // 5. 채팅 명령어: chat 안녕하세요
                if (lower.StartsWith("chat "))
                {
                    string msg = input.Substring(5).Trim();
                    if (!string.IsNullOrEmpty(msg))
                    {
                        await _match.SendChatAsync(msg);
                        AddLog("CHAT", $"<나> \"{msg}\"");
                        Redraw();
                    }
                    continue;
                }

                // 6. 화면 클리어
                if (lower == "clear" || lower == "cls")
                {
                    Redraw();
                    continue;
                }

                // 7. 착수 명령어 파싱 (H8, h8, h 8, git commit -m "H8", kubectl apply -f h8.yaml 등)
                if (TryParseMove(input, out int x, out int y))
                {
                    if (!_match.IsMyTurn)
                    {
                        AddLog("WARN", "내 차례가 아닙니다. 상대방의 착수를 기다리십시오.");
                        Redraw();
                        continue;
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
                    Redraw();
                    continue;
                }

                // 알 수 없는 명령어
                AddLog("WARN", $"알 수 없는 명령: '{input}'. 예: 'H8', 'chat 안녕', 'boss'");
                Redraw();
            }
        }

        /// <summary>다양한 형식(단순 좌표, 깃 커밋, kubectl 등)에서 바둑판 좌표(X, Y)를 추출합니다.</summary>
        private static bool TryParseMove(string input, out int x, out int y)
        {
            x = -1;
            y = -1;

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
                Console.WriteLine("  16:54:12.102 [CSC] Compiling OmokLan.Core.dll (Tier-1 PGO enabled)");
                Console.WriteLine("  16:54:13.491 [DOCKER] Step 8/14 : COPY --from=build /app/publish .");
                Console.WriteLine("  16:54:15.882 [KUBE] Syncing deployment/omok-core to cluster prod-k8s...");
                Console.WriteLine("  16:54:16.012 [INFO] Health probe 200 OK on port 8080. Pod 1 of 6 ready.");
                Console.WriteLine("  16:54:17.451 [INFO] Database migration applied successfully: V20261022__add_index.sql");
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(" [System Busy] 진행 중입니다... (엔터 또는 아무 키를 누르면 원래 콘솔로 복귀)");
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
