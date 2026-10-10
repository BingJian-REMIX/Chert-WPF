using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Chert.Core.Input;

namespace Chert.App.Views;

/// <summary>
/// 清单 #11：触屏面板的按键可视化与交互构建。
/// 运行态（<see cref="TouchOverlayWindow"/>）与布局编辑器共用同一套外观，保证「所见即所得」。
/// </summary>
internal static class TouchPanelBuilder
{
    /// <summary>顶部拖拽把手高度。</summary>
    public const int HandleHeight = 28;

    /// <summary>
    /// 按配置构建按键画布。
    /// </summary>
    /// <param name="cfg">布局配置。</param>
    /// <param name="editMode">true = 布局编辑态（可拖动按键，不发送按键）。</param>
    /// <param name="onDown">按键按下回调（运行态传入向游戏发键的方法）。</param>
    /// <param name="onUp">按键抬起回调。</param>
    /// <param name="releaseHooks">
    /// 运行态传入的清理钩子集合：切换键（<see cref="TouchButtonKind.Toggle"/>）锁住不放时，
    /// 面板被重建或关闭前必须逐条调用这里的钩子，否则游戏里会留下一个一直按住的 Shift。
    /// 每构建一次会往里追加若干钩子，重建前请先清空（由调用方负责释放）。
    /// </param>
    public static Canvas Build(
        TouchControlConfig cfg,
        bool editMode,
        Action<int>? onDown = null,
        Action<int>? onUp = null,
        List<Action>? releaseHooks = null)
    {
        var canvas = new Canvas
        {
            Width = cfg.PanelWidth,
            Height = cfg.PanelHeight,
            Background = editMode
                ? new SolidColorBrush(Color.FromArgb(0x22, 0x80, 0x80, 0x80))
                : new SolidColorBrush(Colors.Transparent)
        };

        foreach (var b in cfg.Buttons)
        {
            var el = CreateButton(b);
            Canvas.SetLeft(el, b.X);
            Canvas.SetTop(el, b.Y);
            canvas.Children.Add(el);

            if (editMode)
            {
                AttachDrag(el, b, cfg, canvas);
                continue;
            }

            AttachPress(el, b, onDown, onUp, releaseHooks);
        }

        return canvas;
    }

    private static Border CreateButton(TouchButtonConfig b)
    {
        // 三类按键各有辨识色：方向键蓝 / 切换键绿 / 动作键橙
        var accent = b.Kind switch
        {
            TouchButtonKind.Direction => Color.FromArgb(0xCC, 0x2F, 0x6F, 0xED),
            TouchButtonKind.Toggle => Color.FromArgb(0xCC, 0x2E, 0x9E, 0x5B),
            _ => Color.FromArgb(0xCC, 0xE0, 0x5A, 0x2A)
        };

        var border = new Border
        {
            Width = b.Size,
            Height = b.Size,
            // 统一方块造型（小圆角），不再按键大小切成圆形
            CornerRadius = new CornerRadius(Math.Min(12, b.Size * 0.18)),
            Background = new SolidColorBrush(accent),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(2),
            Cursor = Cursors.Hand
        };

        var sp = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        sp.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(b.Label) ? b.KeyName : b.Label,
            FontSize = Math.Max(10, b.Size * 0.22),
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        if (!string.IsNullOrWhiteSpace(b.Label) && !string.IsNullOrWhiteSpace(b.KeyName))
        {
            sp.Children.Add(new TextBlock
            {
                Text = b.KeyName,
                FontSize = Math.Max(9, b.Size * 0.16),
                Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0)
            });
        }
        border.Child = sp;
        border.Tag = b;
        return border;
    }

    /// <summary>运行态：按下 / 抬起（含触屏事件），离开控件时兜底抬起，避免按键卡住。</summary>
    private static void AttachPress(
        Border el,
        TouchButtonConfig b,
        Action<int>? onDown,
        Action<int>? onUp,
        List<Action>? releaseHooks)
    {
        var pressed = false;                                       // 手指 / 鼠标是否压着
        var latched = false;                                       // 切换键：是否处于「锁住按住」状态
        var isToggle = b.Kind == TouchButtonKind.Toggle;
        var idleBorder = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF));
        var activeBorder = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xD8, 0x66));

        void SetActive(bool active)
        {
            el.Opacity = active ? 0.65 : 1;
            el.BorderBrush = active ? activeBorder : idleBorder;
            el.BorderThickness = new Thickness(active ? 3 : 2);
        }

        void Down()
        {
            if (pressed) return;
            pressed = true;
            // 切换键已锁住时，再次按住不该重复发一次 keydown（否则某些版本会当成重复触发）
            if (isToggle && latched) return;
            SetActive(true);
            try { onDown?.Invoke(b.Vk); } catch { }
        }

        void Up()
        {
            if (!pressed) return;
            pressed = false;
            if (isToggle)
            {
                // 一次完整点击（按下 + 抬起）翻转锁存：抬起后仍保持按住不发 keyup，下一次点击才松开
                latched = !latched;
                if (latched) return;
            }
            SetActive(false);
            try { onUp?.Invoke(b.Vk); } catch { }
        }

        // 面板重建 / 关闭时释放仍处于锁存态的键，避免游戏里留下一个「永远按住」的键
        releaseHooks?.Add(() =>
        {
            if (!latched) return;
            latched = false;
            pressed = false;
            SetActive(false);
            try { onUp?.Invoke(b.Vk); } catch { }
        });

        el.PreviewMouseDown += (_, e) => { e.Handled = true; Down(); };
        el.PreviewMouseUp += (_, e) => { e.Handled = true; Up(); };
        el.MouseLeave += (_, _) => Up();
        el.LostMouseCapture += (_, _) => Up();
        el.TouchDown += (_, e) => { e.Handled = true; Down(); };
        el.TouchUp += (_, e) => { e.Handled = true; Up(); };
        el.TouchLeave += (_, _) => Up();
        el.StylusDown += (_, e) => { e.Handled = true; Down(); };
        el.StylusUp += (_, e) => { e.Handled = true; Up(); };
    }

    /// <summary>编辑态：在面板内拖动按键，实时回写配置坐标。</summary>
    private static void AttachDrag(Border el, TouchButtonConfig b, TouchControlConfig cfg, Canvas canvas)
    {
        bool dragging = false;
        Point start = default;

        el.PreviewMouseDown += (_, e) =>
        {
            dragging = true;
            start = e.GetPosition(canvas);
            el.CaptureMouse();
            e.Handled = true;
        };
        el.MouseMove += (_, e) =>
        {
            if (!dragging) return;
            var p = e.GetPosition(canvas);
            var nx = Clamp(Canvas.GetLeft(el) + (p.X - start.X), 0, Math.Max(0, cfg.PanelWidth - b.Size));
            var ny = Clamp(Canvas.GetTop(el) + (p.Y - start.Y), 0, Math.Max(0, cfg.PanelHeight - b.Size));
            Canvas.SetLeft(el, nx);
            Canvas.SetTop(el, ny);
            b.X = nx;
            b.Y = ny;
            start = p;
        };
        el.PreviewMouseUp += (_, _) =>
        {
            dragging = false;
            el.ReleaseMouseCapture();
        };
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
}
