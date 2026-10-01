using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Chert.App;

/// <summary>
/// 清单 #17：界面动效工具 —— 对齐 design/chert_layout.html 的 <c>.reveal</c> 错落入场。
/// <para>
/// HTML 的做法是：页面切换时给当前页内的 <c>.card / .pagehead / .set-row / .sitem / .dcard / .pbox</c>
/// 逐个加上 <c>transition-delay: i*25ms</c>，配合 <c>opacity 0→1</c> 与 <c>translateY(12px→0)</c>，
/// 时长 180ms、缓动 <c>cubic-bezier(0.16,1,0.3,1)</c>（近似 expo-out）。
/// </para>
/// <para>
/// WPF 没有 CSS 选择器，这里改用**结构启发式**定位「内容区块」：从页面根元素一路向下穿过
/// 「只有一个子元素」的容器（UserControl → Border → Grid → ScrollViewer …），
/// 第一个拥有多个子元素的容器，它的子元素就是该页的区块列表。
/// 碰到列表类控件（ItemsControl 等）立即停止 —— 那些子项会被虚拟化、重复动画，观感也乱。
/// </para>
/// </summary>
internal static class MotionFX
{
    /// <summary>HTML .reveal 的时长（180ms）。</summary>
    private const int DurationMs = 180;

    /// <summary>HTML 的 transition-delay 步长（25ms）。</summary>
    private const int StepMs = 25;

    /// <summary>最多动画多少个区块（避免深页面里刷出上百条动画）。</summary>
    private const int MaxItems = 14;

    /// <summary>向下穿透的最大层数（UserControl → Border → Grid → ScrollViewer → …）。</summary>
    private const int MaxDepth = 8;

    /// <summary>cubic-bezier(0.16, 1, 0.3, 1) 的近似：起步极快、收尾极缓。</summary>
    private static readonly IEasingFunction RevealEase = new QuinticEase { EasingMode = EasingMode.EaseOut };

    /// <summary>记录由本类创建的位移变换，便于重复调用时复用（而不是层层包裹）。</summary>
    private static readonly DependencyProperty RevealLiftProperty =
        DependencyProperty.RegisterAttached(
            "RevealLift", typeof(TranslateTransform), typeof(MotionFX), new PropertyMetadata(null));

    /// <summary>把 <paramref name="root"/> 内的内容区块做一次错峰淡入 + 上浮入场。</summary>
    public static void Reveal(DependencyObject? root)
    {
        if (root is not FrameworkElement element) return;

        var items = CollectRevealTargets(element);
        if (items.Count == 0) return;

        var i = 0;
        foreach (var item in items)
        {
            var delay = TimeSpan.FromMilliseconds(StepMs * i++);
            var duration = TimeSpan.FromMilliseconds(DurationMs);

            item.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, duration) { BeginTime = delay, EasingFunction = RevealEase });

            // 只在元素本身没有 RenderTransform 时补位移，避免覆盖各页自行设置的变换。
            var lift = ResolveLift(item);
            if (lift is null) continue;
            lift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(12, 0, duration) { BeginTime = delay, EasingFunction = RevealEase });
        }
    }

    /// <summary>取出（或创建）该元素用于入场的位移变换；元素自带其它变换时返回 null（只做淡入）。</summary>
    private static TranslateTransform? ResolveLift(UIElement el)
    {
        if (el.GetValue(RevealLiftProperty) is TranslateTransform mine)
        {
            if (ReferenceEquals(el.RenderTransform, mine)) return mine;
            return null;   // 页面后来自己换了变换，别去抢
        }
        if (el.RenderTransform is not null) return null;

        var lift = new TranslateTransform(0, 0);
        el.RenderTransform = lift;
        el.SetValue(RevealLiftProperty, lift);
        return lift;
    }

    /// <summary>
    /// 定位「内容区块」：一直向下穿过单子元素容器，取第一个多子容器 children（截断到 <see cref="MaxItems"/>）。
    /// </summary>
    private static List<FrameworkElement> CollectRevealTargets(DependencyObject root)
    {
        var result = new List<FrameworkElement>(MaxItems);
        var current = root;

        for (var depth = 0; depth < MaxDepth && current is not null; depth++)
        {
            // 列表类控件内部是被虚拟化 / 复用的数据项，动画它们既无意义又会闪烁。
            // 注意 ItemsControl（ListBox / ListView / ComboBox / TreeView 等）与 Panel 是并列分支，
            // 普通面板（Grid / StackPanel）要继续往下穿透。
            if (current is ItemsControl) break;

            var count = VisualTreeHelper.GetChildrenCount(current);
            if (count == 0) break;

            if (count == 1)
            {
                current = VisualTreeHelper.GetChild(current, 0);
                continue;
            }

            for (var i = 0; i < count && result.Count < MaxItems; i++)
            {
                if (VisualTreeHelper.GetChild(current, i) is FrameworkElement fe &&
                    fe.Visibility == Visibility.Visible)
                {
                    result.Add(fe);
                }
            }
            break;
        }

        return result;
    }
}
