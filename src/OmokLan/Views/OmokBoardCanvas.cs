using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OmokLan.Engine.Core;
using OmokLan.ViewModels;

namespace OmokLan.Views;

/// <summary>
/// 15x15 오목판 격자선, 화점, 바둑돌, 호버 미리보기, 마지막 착수 위치, 승리 라인을 렌더링하는 커스텀 컨트롤입니다.
/// </summary>
public sealed class OmokBoardCanvas : Canvas
{
    private const int BoardSize = 15;
    private int _hoverX = -1;
    private int _hoverY = -1;

    public OmokBoardCanvas()
    {
        ClipToBounds = true;
        Cursor = Cursors.Hand;

        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        SizeChanged += (s, e) => InvalidateVisual();
    }

    public static readonly DependencyProperty GameProperty =
        DependencyProperty.Register(nameof(Game), typeof(GameViewModel), typeof(OmokBoardCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnGamePropertyChanged));

    private static void OnGamePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is OmokBoardCanvas canvas)
        {
            if (e.OldValue is GameViewModel oldVm)
            {
                oldVm.BoardInvalidated -= canvas.OnBoardInvalidated;
            }
            if (e.NewValue is GameViewModel newVm)
            {
                newVm.BoardInvalidated += canvas.OnBoardInvalidated;
            }
            canvas.InvalidateVisual();
        }
    }

    private void OnBoardInvalidated()
    {
        Dispatcher.InvokeAsync(InvalidateVisual);
    }

    public GameViewModel? Game
    {
        get => (GameViewModel?)GetValue(GameProperty);
        set => SetValue(GameProperty, value);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        var (cellSize, offsetX, offsetY) = CalculateMetrics();

        int x = (int)Math.Round((pos.X - offsetX) / cellSize);
        int y = (int)Math.Round((pos.Y - offsetY) / cellSize);

        if (x >= 0 && x < BoardSize && y >= 0 && y < BoardSize)
        {
            if (_hoverX != x || _hoverY != y)
            {
                _hoverX = x;
                _hoverY = y;
                InvalidateVisual();
            }
        }
        else
        {
            ClearHover();
        }
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        ClearHover();
    }

    private void ClearHover()
    {
        if (_hoverX != -1 || _hoverY != -1)
        {
            _hoverX = -1;
            _hoverY = -1;
            InvalidateVisual();
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Game is null || !Game.IsMyTurn || Game.IsOver || Game.IsCoinTossing || Game.IsPaused) return;

        var pos = e.GetPosition(this);
        var (cellSize, offsetX, offsetY) = CalculateMetrics();

        int x = (int)Math.Round((pos.X - offsetX) / cellSize);
        int y = (int)Math.Round((pos.Y - offsetY) / cellSize);

        if (x >= 0 && x < BoardSize && y >= 0 && y < BoardSize)
        {
            var cell = Game.BoardCells[x, y];
            if (cell.State == Stone.None)
            {
                if (Game.PlaceCommand.CanExecute(cell))
                {
                    Game.PlaceCommand.Execute(cell);
                    InvalidateVisual();
                }
            }
        }
    }

    private (double CellSize, double OffsetX, double OffsetY) CalculateMetrics()
    {
        double w = ActualWidth;
        double h = ActualHeight;
        double size = Math.Min(w, h);
        if (size <= 0) return (30, 0, 0);

        double margin = 32;
        double boardSpan = size - (margin * 2);
        double cellSize = boardSpan / (BoardSize - 1);

        double offsetX = (w - boardSpan) / 2.0;
        double offsetY = (h - boardSpan) / 2.0;

        return (cellSize, offsetX, offsetY);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var (cellSize, offsetX, offsetY) = CalculateMetrics();

        // 1. 모던 우드/다크 보드 바탕
        var boardRect = new Rect(offsetX - 24, offsetY - 24, (BoardSize - 1) * cellSize + 48, (BoardSize - 1) * cellSize + 48);
        var boardBg = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString("#1F2438"),
            (Color)ColorConverter.ConvertFromString("#131728"),
            new Point(0, 0), new Point(1, 1));
        var boardBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A4268"));

        dc.DrawRoundedRectangle(boardBg, new Pen(boardBorder, 2), boardRect, 18, 18);

        // 2. 격자선
        var gridPen = new Pen(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#47527A")), 1.2);
        gridPen.Freeze();

        for (int i = 0; i < BoardSize; i++)
        {
            double pos = i * cellSize;
            // 가로선
            dc.DrawLine(gridPen, new Point(offsetX, offsetY + pos), new Point(offsetX + (BoardSize - 1) * cellSize, offsetY + pos));
            // 세로선
            dc.DrawLine(gridPen, new Point(offsetX + pos, offsetY), new Point(offsetX + pos, offsetY + (BoardSize - 1) * cellSize));
        }

        // 3. 화점 (Star Points)
        int[] stars = { 3, 7, 11 };
        var starBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#818CF8"));
        starBrush.Freeze();
        foreach (int sx in stars)
        {
            foreach (int sy in stars)
            {
                dc.DrawEllipse(starBrush, null, new Point(offsetX + sx * cellSize, offsetY + sy * cellSize), 4, 4);
            }
        }

        if (Game is null) return;

        // 4. 돌 렌더링
        double stoneRadius = cellSize * 0.44;

        // 흑돌 브러시
        var blackBrush = new RadialGradientBrush
        {
            Center = new Point(0.35, 0.35),
            GradientOrigin = new Point(0.35, 0.35),
            RadiusX = 0.7,
            RadiusY = 0.7,
            GradientStops =
            {
                new GradientStop((Color)ColorConverter.ConvertFromString("#4B5563"), 0),
                new GradientStop((Color)ColorConverter.ConvertFromString("#111827"), 0.7),
                new GradientStop((Color)ColorConverter.ConvertFromString("#030712"), 1.0)
            }
        };
        blackBrush.Freeze();

        // 백돌 브러시
        var whiteBrush = new RadialGradientBrush
        {
            Center = new Point(0.35, 0.35),
            GradientOrigin = new Point(0.35, 0.35),
            RadiusX = 0.7,
            RadiusY = 0.7,
            GradientStops =
            {
                new GradientStop((Color)ColorConverter.ConvertFromString("#FFFFFF"), 0),
                new GradientStop((Color)ColorConverter.ConvertFromString("#E2E8F0"), 0.65),
                new GradientStop((Color)ColorConverter.ConvertFromString("#94A3B8"), 1.0)
            }
        };
        whiteBrush.Freeze();

        var stoneShadowBrush = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));
        stoneShadowBrush.Freeze();

        for (int y = 0; y < BoardSize; y++)
        {
            for (int x = 0; x < BoardSize; x++)
            {
                var cell = Game.BoardCells[x, y];
                if (cell.State != Stone.None)
                {
                    var pt = new Point(offsetX + x * cellSize, offsetY + y * cellSize);
                    var shadowPt = new Point(pt.X + 2, pt.Y + 3);

                    // 돌 그림자
                    dc.DrawEllipse(stoneShadowBrush, null, shadowPt, stoneRadius, stoneRadius);

                    // 바둑돌
                    var brush = cell.State == Stone.Black ? blackBrush : whiteBrush;
                    dc.DrawEllipse(brush, null, pt, stoneRadius, stoneRadius);

                    // 마지막 수 표시 (골드 링)
                    if (cell.IsLastMove)
                    {
                        var lastPen = new Pen(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")), 2.2);
                        dc.DrawEllipse(null, lastPen, pt, stoneRadius * 0.5, stoneRadius * 0.5);
                    }

                    // 승리 라인 하이라이트 (네온 민트 글로우 링)
                    if (cell.IsWinLine)
                    {
                        var winPen = new Pen(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399")), 3.5);
                        dc.DrawEllipse(null, winPen, pt, stoneRadius * 0.85, stoneRadius * 0.85);
                    }
                }
            }
        }

        // 5. 마우스 호버 반투명 돌 미리보기 (내 턴일 때만)
        if (Game.IsMyTurn && !Game.IsOver && !Game.IsCoinTossing && !Game.IsPaused && _hoverX != -1 && _hoverY != -1)
        {
            if (Game.BoardCells[_hoverX, _hoverY].State == Stone.None)
            {
                var pt = new Point(offsetX + _hoverX * cellSize, offsetY + _hoverY * cellSize);
                var ghostColor = Game.MyColor == Stone.Black
                    ? Color.FromArgb(120, 20, 20, 30)
                    : Color.FromArgb(140, 255, 255, 255);
                var ghostBrush = new SolidColorBrush(ghostColor);
                ghostBrush.Freeze();

                dc.DrawEllipse(ghostBrush, null, pt, stoneRadius * 0.9, stoneRadius * 0.9);
            }
        }
    }
}
