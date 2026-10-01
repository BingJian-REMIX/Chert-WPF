using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Chert.App;

/// <summary>
/// 进度条（<see cref="Slider"/>）的「点击 / 拖动 → 跳转」交互。
/// <para>
/// 为什么不直接监听 <c>MouseLeftButtonUp</c> 再读 <c>Slider.Value</c>：
/// Slider 在模板内部由 RepeatButton / Thumb 处理鼠标，隧道与冒泡的先后顺序随模板而定，
/// 时序不可靠（曾出现「点了没反应 / 提交的是旧值」）。这里干脆完全接管鼠标：
/// 按下即进入拖拽、由坐标直接算值、松手时提交，时序确定且不依赖具体模板实现。
/// </para>
/// <para>
/// 拖拽期间通过 <paramref name="dragStart"/> / <paramref name="dragEnd"/> 通知宿主，
/// 例如让 ViewModel 置 <c>IsSeeking</c> 暂停定时器回写位置，避免拖柄在拖动过程中回弹。
/// </para>
/// </summary>
public static class SeekInteraction
{
    /// <summary>为 <paramref name="slider"/> 挂上点击 / 拖动跳转。</summary>
    /// <param name="slider">目标进度条。</param>
    /// <param name="commit">松手时回调，参数为 0–100 的比例值。</param>
    /// <param name="dragStart">开始拖拽时回调（可空）。</param>
    /// <param name="dragEnd">结束拖拽时回调（可空）。</param>
    public static void Attach(Slider slider, Action<double> commit,
        Action? dragStart = null, Action? dragEnd = null)
    {
        ArgumentNullException.ThrowIfNull(slider);
        ArgumentNullException.ThrowIfNull(commit);

        var dragging = false;

        // 由鼠标坐标换算成值。WPF 的 Track 有个 internal 的 ValueFromPoint，外部调不到，
        // 这里用公开的 Thumb 尺寸复刻同一套算法：
        // 拖柄的「可用行程」= 轨道长度 - 拖柄宽度，坐标减去半个拖柄即得行程内的偏移量。
        void ApplyFromPoint(Point point)
        {
            // 注意：本工程有 global using Track = Chert.Core.Toolbox.Track 的别名（曲目模型），
            // 会遮蔽 WPF 的 Track，故此处必须写全限定名。
            if (slider.Template?.FindName("PART_Track", slider)
                is not System.Windows.Controls.Primitives.Track track) return;
            if (slider.Orientation != Orientation.Horizontal) return; // 项目内进度条均为横向

            var thumbWidth = track.Thumb?.ActualWidth ?? 0;
            var travel = track.ActualWidth - thumbWidth;
            if (travel <= 0)
            {
                slider.SetCurrentValue(RangeBase.ValueProperty, slider.Minimum);
                return;
            }

            // 轨道与滑块同宽、同左边缘，故鼠标相对滑块的 X 可直接用于换算。
            var offset = point.X - thumbWidth / 2;
            var ratio = Math.Clamp(offset / travel, 0, 1);
            var value = slider.Minimum + ratio * (slider.Maximum - slider.Minimum);

            // SetCurrentValue：只改「当前值」，不会把 ProgressRatio / Volume 的绑定顶掉。
            slider.SetCurrentValue(RangeBase.ValueProperty, value);
        }

        void Finish()
        {
            if (!dragging) return;
            dragging = false;
            if (slider.IsMouseCaptured) slider.ReleaseMouseCapture();
            dragEnd?.Invoke();
            commit(slider.Value);
        }

        slider.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left) return;

            dragging = true;
            dragStart?.Invoke();
            slider.CaptureMouse();
            ApplyFromPoint(e.GetPosition(slider));
            e.Handled = true; // 阻断模板自身的 RepeatButton / Thumb 处理，交互只由这里负责
        };

        slider.MouseMove += (_, e) =>
        {
            if (dragging) ApplyFromPoint(e.GetPosition(slider));
        };

        slider.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!dragging) return;
            ApplyFromPoint(e.GetPosition(slider));
            Finish();
            e.Handled = true;
        };

        // 鼠标在别处松开 / 失去捕获（Alt+Tab 等）时也要收尾，否则会一直卡在「拖拽中」。
        slider.LostMouseCapture += (_, _) => Finish();
    }
}
