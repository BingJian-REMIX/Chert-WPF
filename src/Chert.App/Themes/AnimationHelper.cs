using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Chert.App.Themes;

/// <summary>
/// 统一的入场动画工具：弹窗 / 卡片淡入 + 上移缓出。
/// 全部受 <see cref="MainWindow.AnimationsEnabled"/> 全局开关控制——
/// 关闭动画时直接显示终态，不做任何位移与透明度过渡。
/// 用法（弹窗 Loaded 事件）：
/// <code>AnimationHelper.PlayModalEnter(Card);</code>
/// </summary>
public static class AnimationHelper
{
    /// <summary>入场位移距离（像素，从下往上滑入）。</summary>
    private const double EnterOffset = 24;
    private static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(240);

    public static void PlayModalEnter(FrameworkElement element)
    {
        if (element == null) return;

        // 开关关闭：直接落到终态，避免任何残留的半透明 / 偏移。
        if (!Chert.App.MainWindow.AnimationsEnabled)
        {
            element.Opacity = 1;
            element.RenderTransform = null;
            return;
        }

        var transform = element.RenderTransform as TranslateTransform;
        if (transform == null)
        {
            transform = new TranslateTransform();
            element.RenderTransform = transform;
        }

        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        element.Opacity = 0;
        transform.Y = EnterOffset;

        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, EnterDuration) { EasingFunction = ease });
        transform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(EnterOffset, 0, EnterDuration) { EasingFunction = ease });
    }
}
