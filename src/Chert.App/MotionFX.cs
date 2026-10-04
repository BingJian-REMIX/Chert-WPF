using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

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

    // ===== 横向滑动切换（从右淡入 / 向左淡出），用于设置分类与全局页面 =====

    /// <summary>横向滑动距离（像素）。新内容从右滑入、旧内容向左滑出。</summary>
    private const double SlideOffsetX = 32;

    /// <summary>横向滑动时长（ms）。</summary>
    private const int SlideMs = 220;

    /// <summary>记录本类创建的滑动位移变换，便于重复调用时复用与清理。</summary>
    private static readonly DependencyProperty SlideTransformProperty =
        DependencyProperty.RegisterAttached(
            "SlideTransform", typeof(TranslateTransform), typeof(MotionFX), new PropertyMetadata(null));

    /// <summary>取出（或创建）该元素用于横向滑动的位移变换；元素自带其它变换时返回 null（退化为纯淡入/淡出）。</summary>
    private static TranslateTransform? ResolveSlideTransform(UIElement el)
    {
        if (el.GetValue(SlideTransformProperty) is TranslateTransform mine)
        {
            if (ReferenceEquals(el.RenderTransform, mine)) return mine;
            return null;   // 页面后来自己换了变换，别去抢
        }
        if (el.RenderTransform is not null) return null;

        var tf = new TranslateTransform(0, 0);
        el.RenderTransform = tf;
        el.SetValue(SlideTransformProperty, tf);
        return tf;
    }

    /// <summary>清理滑动变换，复位元素 RenderTransform（避免残留位移影响布局/命中）。</summary>
    private static void ClearSlideTransform(UIElement el, TranslateTransform? tf)
    {
        if (tf is null) return;
        tf.BeginAnimation(TranslateTransform.XProperty, null);
        if (ReferenceEquals(el.RenderTransform, tf))
            el.RenderTransform = null;
        el.ClearValue(SlideTransformProperty);
    }

    /// <summary>
    /// 让元素从右侧滑入并淡入（从右向左）。若元素已自带 RenderTransform 则退化为纯淡入。
    /// 关闭动画时直接落到终态。
    /// </summary>
    public static void SlideInFromRight(FrameworkElement element)
    {
        if (element is null) return;
        var tf = ResolveSlideTransform(element);
        if (!MainWindow.AnimationsEnabled)
        {
            element.Opacity = 1;
            ClearSlideTransform(element, tf);
            return;
        }

        var dur = TimeSpan.FromMilliseconds(SlideMs);
        element.Opacity = 0;
        if (tf is not null) tf.X = SlideOffsetX;

        // 收尾必须「先清动画、再落本地值」：FillBehavior.HoldEnd 会让动画**永久接管** Opacity，
        // 之后任何 element.Opacity = x 都只是改本地值、被动画压住（表现为内容再也显示不出来）。
        // 原实现只在 tf != null 时收尾，元素自带 RenderTransform（tf == null）时 Opacity 动画会永久残留。
        var finished = false;
        void Finish()
        {
            if (finished) return;
            finished = true;
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            ClearSlideTransform(element, tf);
        }

        var opacityAnim = new DoubleAnimation(0, 1, dur) { EasingFunction = RevealEase, FillBehavior = FillBehavior.HoldEnd };
        opacityAnim.Completed += (_, _) => Finish();
        element.BeginAnimation(UIElement.OpacityProperty, opacityAnim);

        if (tf is not null)
        {
            var slide = new DoubleAnimation(SlideOffsetX, 0, dur) { EasingFunction = RevealEase, FillBehavior = FillBehavior.HoldEnd };
            tf.BeginAnimation(TranslateTransform.XProperty, slide);
        }
    }

    /// <summary>
    /// 让元素向左滑出并淡出；动画结束后回调 <paramref name="onCompleted"/>（用于旧内容退场）。
    /// 若元素已自带 RenderTransform 则退化为纯淡出。关闭动画时直接回调。
    /// </summary>
    public static void SlideOutToLeft(FrameworkElement element, Action? onCompleted = null)
    {
        if (element is null) { onCompleted?.Invoke(); return; }
        var tf = ResolveSlideTransform(element);
        if (!MainWindow.AnimationsEnabled)
        {
            element.Opacity = 1;
            ClearSlideTransform(element, tf);
            onCompleted?.Invoke();
            return;
        }

        var dur = TimeSpan.FromMilliseconds(SlideMs);
        var opacityAnim = new DoubleAnimation(1, 0, dur) { EasingFunction = RevealEase, FillBehavior = FillBehavior.HoldEnd };

        if (tf is not null)
        {
            var slide = new DoubleAnimation(0, -SlideOffsetX, dur) { EasingFunction = RevealEase, FillBehavior = FillBehavior.HoldEnd };
            slide.Completed += (_, _) => { ClearSlideTransform(element, tf); onCompleted?.Invoke(); };
            tf.BeginAnimation(TranslateTransform.XProperty, slide);
        }
        else
        {
            opacityAnim.Completed += (_, _) => onCompleted?.Invoke();
        }

        element.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
    }

    // ===== 副标签（侧边栏二级项）切换 =====

    /// <summary>
    /// 副标签切换：让新内容整块从右侧滑入并淡入（与设置页分类切换同一观感）。
    /// 下载页 / 工具箱页的副标签由数据绑定驱动，内容控件在属性变更的当帧还没有实际尺寸，
    /// 因此必须延迟到 <see cref="DispatcherPriority.Loaded"/> 再播放 —— 与 MainWindow 切页的 Reveal 同一处理方式。
    /// </summary>
    public static void SlideInSubTab(FrameworkElement? element)
    {
        if (element is null || !MainWindow.AnimationsEnabled) return;

        if (element.Dispatcher.CheckAccess())
            element.Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                new Action(() => SlideInFromRight(element)));
        else
            SlideInFromRight(element);
    }
}
