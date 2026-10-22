using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OmokLan.Engine.Core;
using OmokLan.ViewModels;

namespace OmokLan.Views;

public partial class CoinTossControl : UserControl
{
    public CoinTossControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PlayTossAnimation();
    }

    public void PlayTossAnimation()
    {
        if (DataContext is not GameViewModel vm) return;

        bool targetFront = vm.CoinWinnerColor == vm.MyColor;

        // 1. 위아래 포물선 점프 애니메이션 (Bounce)
        var bounceAnimation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(3.2)
        };
        // 솟구침
        bounceAnimation.KeyFrames.Add(new SplineDoubleKeyFrame(-60, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.6))));
        bounceAnimation.KeyFrames.Add(new SplineDoubleKeyFrame(-80, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.2))));
        bounceAnimation.KeyFrames.Add(new SplineDoubleKeyFrame(-40, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.0))));
        bounceAnimation.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.8))));

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

        // 3. 플립 회전 (ScaleX 를 빠르게 1 -> 0 -> 1 -> 0 변환)
        // 렌더 프레임 또는 타이머로 플립 카운트를 제어
        int flips = 0;
        int totalFlips = targetFront ? 16 : 17; // 홀수면 뒷면, 짝수면 앞면으로 종료
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
                // 최종 착지 상태 확정
                FrontFace.Visibility = targetFront ? Visibility.Visible : Visibility.Collapsed;
                BackFace.Visibility = targetFront ? Visibility.Collapsed : Visibility.Visible;

                // 마지막 착지 임팩트 셰이크
                var landAnim = new DoubleAnimation
                {
                    From = 1.15,
                    To = 1.0,
                    Duration = TimeSpan.FromSeconds(0.3),
                    EasingFunction = new BounceEase { Bounces = 2, Bounciness = 4 }
                };
                CoinScaleX.BeginAnimation(ScaleTransform.ScaleXProperty, landAnim);
                CoinScaleX.BeginAnimation(ScaleTransform.ScaleYProperty, landAnim);
            }
            else if (flips > totalFlips - 6)
            {
                // 감속 (점점 느려짐)
                flipTimer.Interval = TimeSpan.FromMilliseconds(flipTimer.Interval.TotalMilliseconds * 1.35);
            }
        };

        flipTimer.Start();
    }
}
