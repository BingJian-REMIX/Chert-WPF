using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Chert.App.ViewModels;

namespace Chert.App.Views;

/// <summary>音乐播放器面板（工具箱）。播放状态与命令来自 <see cref="MusicPlayerViewModel"/> 单例；
/// 实际解码由主窗口注入的 MediaElement 宿主完成，本面板只负责展示与交互。</summary>
public partial class MusicPlayerView : UserControl
{
    public MusicPlayerView()
    {
        InitializeComponent();
        DataContext = MusicPlayerViewModel.Instance;
        // bug #10 + P13：点击 / 拖动进度条跳转。统一走 SeekInteraction —— 完全接管鼠标时序，
        // 由坐标算值并在松手时提交；拖拽期间置 IsSeeking，暂停定时器回写位置，避免拖柄回弹。
        SeekInteraction.Attach(
            SeekBar,
            ratio =>
            {
                if (MusicPlayerViewModel.Instance.HasProgress)
                    MusicPlayerViewModel.Instance.SeekCommand.Execute(ratio);
            },
            () => MusicPlayerViewModel.Instance.IsSeeking = true,
            () => MusicPlayerViewModel.Instance.IsSeeking = false);

        // 歌词换行时播过渡动画。VM 侧已保证「内容不变不发通知」，故这里无需自己去重。
        MusicPlayerViewModel.Instance.PropertyChanged += OnLyricTextChanged;
    }

    /// <summary>歌词切换动画的时长（毫秒）。规格只要求「淡入淡出 + 轻微上移」，未指定时长。</summary>
    private const int LyricFadeMs = 260;

    /// <summary>上移距离（像素）。刻意很小 —— 幅度大了会像卡拉 OK 滚动，反而抢注意力。</summary>
    private const double LyricRisePx = 7;

    private string _lastLyricText = "";

    /// <summary>
    /// 歌词行切换时播放「淡出下沉 → 淡入上移」，规格实现项 4。
    /// </summary>
    /// <remarks>
    /// VM 侧已经做到「内容不变不发通知」，所以这里每次触发都是真的换行了，
    /// 不需要自己去重。
    /// </remarks>
    private void OnLyricTextChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MusicPlayerViewModel.LyricText)) return;

        var text = MusicPlayerViewModel.Instance.LyricText ?? "";
        var boxes = new[]
        {
            LyricBox,
            LyricBoxClient,
        };

        // 首次拿到内容：直接落位，不播入场（否则页面初次显示会看到一次无意义的淡入）
        if (string.IsNullOrEmpty(_lastLyricText))
        {
            _lastLyricText = text;
            return;
        }

        foreach (var box in boxes)
        {
            if (box is null) continue;
            try { PlayLyricTransition(box); }
            catch { /* 动画属装饰性，失败不影响歌词显示 */ }
        }
        _lastLyricText = text;
    }

    /// <summary>对单个歌词 TextBlock 播一次「下沉淡出 → 上移淡入」。</summary>
    private static void PlayLyricTransition(FrameworkElement box)
    {
        var dur = TimeSpan.FromMilliseconds(LyricFadeMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // 清掉上一轮动画（否则连续切行会在旧动画上叠加，出现抖动）
        box.BeginAnimation(UIElement.OpacityProperty, null);
        box.BeginAnimation(TranslateTransform.YProperty, null);

        // 前半段：淡出并轻微下沉
        box.Opacity = 1;
        var lift = GetOrCreateLift(box);
        lift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, LyricRisePx * 0.4, TimeSpan.FromMilliseconds(LyricFadeMs * 0.45))
            { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        box.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(1, 0.15, TimeSpan.FromMilliseconds(LyricFadeMs * 0.45))
            { EasingFunction = ease });

        // 后半段：从下沉处淡入并回到原位
        var fadeIn = new DoubleAnimation(1, dur)
        {
            BeginTime = TimeSpan.FromMilliseconds(LyricFadeMs * 0.45),
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd,
        };
        box.Opacity = 0.15;
        box.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        lift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(LyricRisePx * 0.4, 0, dur)
            { BeginTime = TimeSpan.FromMilliseconds(LyricFadeMs * 0.45), EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd });
    }

    /// <summary>取（必要时创建）歌词框的上移变换。动画要作用在 Transform 上，不能直接动 LayoutTransform。</summary>
    private static TranslateTransform GetOrCreateLift(FrameworkElement box)
    {
        if (box.RenderTransform is TranslateTransform t) return t;

        // 客户端模式的歌词框没预设 RenderTransform，这里补一个
        var nt = new TranslateTransform();
        box.RenderTransform = nt;
        return nt;
    }
}
