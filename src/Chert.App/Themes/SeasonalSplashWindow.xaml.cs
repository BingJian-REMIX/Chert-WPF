using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Chert.Core.Utils;

namespace Chert.App.Themes;

/// <summary>
/// 清单 #27：节日启动画面（开屏）。
/// 仅在某个节日档期生效时展示：淡入 → 停留 → 淡出 → 自动关闭。
/// 全程无阻塞、无交互（点击任意处立即关闭），任何异常都不影响启动器主流程。
/// </summary>
public partial class SeasonalSplashWindow : Window
{
    private readonly DispatcherTimer _closeTimer = new();

    public SeasonalSplashWindow(string seasonTitle, string greeting)
    {
        InitializeComponent();

        SeasonText.Text = seasonTitle;
        GreetingText.Text = greeting;
        LauncherText.Text = $"{GameConstants.LauncherDisplayName} v{GameConstants.LauncherVersion}";

        // 点击任意处立即关闭，避免用户等待
        MouseLeftButtonUp += (_, _) => FadeOut();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)));

            _closeTimer.Interval = TimeSpan.FromMilliseconds(1500);
            _closeTimer.Tick += (_, _) => FadeOut();
            _closeTimer.Start();
        }
        catch
        {
            // 动画失败则直接关闭，不做停留
            Close();
        }
    }

    private bool _closing;

    private void FadeOut()
    {
        if (_closing) return;
        _closing = true;

        try { _closeTimer.Stop(); } catch { /* 忽略 */ }

        try
        {
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(320));
            fade.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fade);
        }
        catch
        {
            Close();
        }
    }
}
