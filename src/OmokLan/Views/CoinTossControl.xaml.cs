using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OmokLan.Engine.Core;
using OmokLan.ViewModels;

namespace OmokLan.Views;

public partial class CoinTossControl : UserControl
{
    private GameViewModel? _boundVm;

    public CoinTossControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        IsVisibleChanged += OnIsVisibleChanged;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_boundVm is not null)
        {
            _boundVm.CoinTossTriggered -= OnCoinTossTriggered;
        }

        if (e.NewValue is GameViewModel vm)
        {
            _boundVm = vm;
            _boundVm.CoinTossTriggered += OnCoinTossTriggered;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TryPlayAnimation();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (Visibility == Visibility.Visible)
        {
            TryPlayAnimation();
        }
    }

    private void OnCoinTossTriggered(Stone myColor)
    {
        Dispatcher.InvokeAsync(() => PlayTossAnimation(myColor));
    }

    private void TryPlayAnimation()
    {
        if (DataContext is GameViewModel vm && vm.MyColor != Stone.None)
        {
            PlayTossAnimation(vm.MyColor);
        }
    }

    public void PlayTossAnimation(Stone myColor)
    {
        // 내가 흑(선공)이면 앞면(흑돌)으로 착지, 백(후공)이면 뒷면(백돌)으로 착지
        bool targetFront = (myColor == Stone.Black);

        // 1. 위아래 포물선 점프 애니메이션 (Bounce)
        var bounceAnimation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(3.0)
        };
        bounceAnimation.KeyFrames.Add(new SplineDoubleKeyFrame(-60, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5))));
        bounceAnimation.KeyFrames.Add(new SplineDoubleKeyFrame(-85, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.1))));
        bounceAnimation.KeyFrames.Add(new SplineDoubleKeyFrame(-40, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.9))));
        bounceAnimation.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.6))));

        CoinBounceY.BeginAnimation(TranslateTransform.YProperty, bounceAnimation);

        // 2. 황금 아우라 펄스
        var auraAnim = new DoubleAnimation
        {
            From = 0.9,
            To = 1.25,
            Duration = TimeSpan.FromSeconds(0.4),
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(7)
        };
        AuraScale.BeginAnimation(ScaleTransform.ScaleXProperty, auraAnim);
        AuraScale.BeginAnimation(ScaleTransform.ScaleYProperty, auraAnim);

        // 3. 플립 회전 (ScaleX를 1 -> 0 -> 1 변환하며 앞뒤 전환)
        int flips = 0;
        // targetFront(흑)이면 짝수 개 플립 후 앞면 착지, 후공(백)이면 홀수 개 플립 후 뒷면 착지
        int totalFlips = targetFront ? 18 : 19;
        var flipTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };

        flipTimer.Tick += (s, ev) =>
        {
            flips++;

            // 앞뒤 전환
            bool showFront = (flips % 2) == 0;
            FrontFace.Visibility = showFront ? Visibility.Visible : Visibility.Collapsed;
            BackFace.Visibility = showFront ? Visibility.Collapsed : Visibility.Visible;

            if (flips >= totalFlips)
            {
                flipTimer.Stop();
                // 최종 착지 상태 확정 (내 돌 색상으로 정확히 고정)
                FrontFace.Visibility = targetFront ? Visibility.Visible : Visibility.Collapsed;
                BackFace.Visibility = targetFront ? Visibility.Collapsed : Visibility.Visible;

                // 마지막 착지 임팩트 셰이크
                var landAnim = new DoubleAnimation
                {
                    From = 1.15,
                    To = 1.0,
                    Duration = TimeSpan.FromSeconds(0.35),
                    EasingFunction = new BounceEase { Bounces = 2, Bounciness = 3 }
                };
                CoinScaleX.BeginAnimation(ScaleTransform.ScaleXProperty, landAnim);
                CoinScaleX.BeginAnimation(ScaleTransform.ScaleYProperty, landAnim);
            }
            else if (flips > totalFlips - 6)
            {
                // 감속
                flipTimer.Interval = TimeSpan.FromMilliseconds(flipTimer.Interval.TotalMilliseconds * 1.35);
            }
        };

        flipTimer.Start();
    }
}
