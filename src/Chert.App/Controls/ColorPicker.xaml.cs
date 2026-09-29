using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Chert.App.Controls;

/// <summary>
/// 全色域取色器（清单 #50）。
/// 交互约定：上方色域平面横向拖动选色相、纵向拖动选饱和度；下方独立滑块拖选亮度；
/// 底部附带完整色板。控件自身不含业务语义，主题色选择（清单 #59）直接复用同一实例。
/// </summary>
public partial class ColorPicker : UserControl
{
    public static readonly DependencyProperty SelectedColorProperty =
        DependencyProperty.Register(
            nameof(SelectedColor), typeof(Color), typeof(ColorPicker),
            new FrameworkPropertyMetadata(Colors.White,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedColorChanged));

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    private const int PlaneW = 180;
    private const int PlaneH = 132;

    private readonly WriteableBitmap _planeBitmap =
        new(PlaneW, PlaneH, 96, 96, PixelFormats.Bgra32, null);

    // HSL 为内部权威值：RGB 往返在纯白 / 纯黑 / 灰色处会丢失 H、S，
    // 若以 RGB 为准会让指针在极端明度下乱跳，故独立保存。
    private double _h;
    private double _s = 1.0;
    private double _l = ColorMath.PlaneLightness;

    private bool _syncing;
    private bool _planeDragging;
    private bool _lightnessDragging;

    public ColorPicker()
    {
        InitializeComponent();
        PlaneImage.Source = _planeBitmap;
        RenderPlane();
        BuildPalette();
        Loaded += (_, _) =>
        {
            _syncing = true;
            ColorMath.ToHsl(SelectedColor, out _h, out _s, out _l);
            _syncing = false;
            SyncUi();
        };
    }

    // ---- 渲染 ----

    /// <summary>
    /// 绘制色域位图。平面固定使用 <see cref="ColorMath.PlaneLightness"/> 明度，
    /// 亮度交由下方独立滑块控制，这样即使用户把明度拉到极端，平面仍保持完整鲜艳色域可选。
    /// </summary>
    private void RenderPlane()
    {
        var px = new byte[PlaneW * PlaneH * 4];
        for (var y = 0; y < PlaneH; y++)
        {
            var s = 1.0 - y / (double)(PlaneH - 1);
            for (var x = 0; x < PlaneW; x++)
            {
                var c = ColorMath.FromHsl(x / (double)(PlaneW - 1), s, ColorMath.PlaneLightness);
                var i = (y * PlaneW + x) * 4;
                px[i] = c.B;
                px[i + 1] = c.G;
                px[i + 2] = c.R;
                px[i + 3] = 0xFF;
            }
        }
        _planeBitmap.WritePixels(new Int32Rect(0, 0, PlaneW, PlaneH), px, PlaneW * 4, 0);
    }

    /// <summary>完整色板：10 个色相 × 7 档明度/饱和度 + 末行灰度。</summary>
    private void BuildPalette()
    {
        const int hueCount = 10;
        (double S, double L)[] levels =
        {
            (0.30, 0.94), (0.50, 0.86), (0.72, 0.76), (1.00, 0.63),
            (1.00, 0.50), (0.92, 0.38), (0.78, 0.27),
        };

        foreach (var (s, l) in levels)
        for (var i = 0; i < hueCount; i++)
            AddSwatch(ColorMath.FromHsl(i / (double)hueCount, s, l));

        // 末行：10 级灰度
        for (var i = 0; i < hueCount; i++)
        {
            var v = (byte)Math.Round(255.0 * (hueCount - 1 - i) / (hueCount - 1));
            AddSwatch(Color.FromRgb(v, v, v));
        }
    }

    private void AddSwatch(Color c)
    {
        var border = new Border
        {
            Width = 16, Height = 14, Margin = new Thickness(1),
            Background = new SolidColorBrush(c),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };
        border.MouseLeftButtonDown += (_, _) => PickSwatch(c);
        PaletteHost.Children.Add(border);
    }

    private void PickSwatch(Color c)
    {
        ColorMath.ToHsl(c, out var h, out var s, out var l);
        _h = s < 1e-4 ? _h : h;
        _s = s < 1e-4 ? 0.0 : s;
        _l = l;
        ApplyColor();
    }

    // ---- 交互 ----

    private void Plane_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _planeDragging = true;
        PlaneBorder.CaptureMouse();
        UpdateFromPlane(e.GetPosition(PlaneBorder));
        e.Handled = true;
    }

    private void Plane_MouseMove(object sender, MouseEventArgs e)
    {
        if (_planeDragging) UpdateFromPlane(e.GetPosition(PlaneBorder));
    }

    private void Plane_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _planeDragging = false;
        PlaneBorder.ReleaseMouseCapture();
    }

    private void UpdateFromPlane(Point p)
    {
        _h = Math.Clamp(p.X / (PlaneW - 1), 0.0, 1.0);
        _s = Math.Clamp(1.0 - p.Y / (PlaneH - 1), 0.0, 1.0);
        ApplyColor();
    }

    private void Lightness_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _lightnessDragging = true;
        LightnessBar.CaptureMouse();
        UpdateFromLightness(e.GetPosition(LightnessBar));
        e.Handled = true;
    }

    private void Lightness_MouseMove(object sender, MouseEventArgs e)
    {
        if (_lightnessDragging) UpdateFromLightness(e.GetPosition(LightnessBar));
    }

    private void Lightness_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _lightnessDragging = false;
        LightnessBar.ReleaseMouseCapture();
    }

    private void UpdateFromLightness(Point p)
    {
        var w = LightnessBar.ActualWidth > 4 ? LightnessBar.ActualWidth - 2 : 1;
        _l = Math.Clamp((p.X - 1) / w, 0.0, 1.0);
        ApplyColor();
    }

    private void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitHex();
    }

    private void HexBox_LostFocus(object sender, RoutedEventArgs e) => CommitHex();

    private void CommitHex()
    {
        if (!ColorMath.TryParseHex(HexBox.Text, out var c)) { SyncUi(); return; }
        ColorMath.ToHsl(c, out _h, out _s, out _l);
        ApplyColor(false);
        SyncUi();
    }

    // ---- 状态同步 ----

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ColorPicker picker || picker._syncing) return;
        if (e.NewValue is not Color c) return;
        ColorMath.ToHsl(c, out picker._h, out picker._s, out picker._l);
        picker.SyncUi();
    }

    private void ApplyColor(bool updateHex = true)
    {
        var c = ColorMath.FromHsl(_h, _s, _l);
        _syncing = true;
        try { SetValue(SelectedColorProperty, c); }
        finally { _syncing = false; }
        SyncUi(updateHex);
    }

    private void SyncUi(bool updateHex = true)
    {
        if (PreviewBorder is null || Pointer is null) return;

        PreviewBorder.Background = new SolidColorBrush(SelectedColor);
        if (updateHex) HexBox.Text = ColorMath.ToHex(SelectedColor);

        Pointer.Margin = new Thickness(
            Math.Clamp(_h * (PlaneW - 1) - 6, -6, PlaneW - 6),
            Math.Clamp((1.0 - _s) * (PlaneH - 1) - 6, -6, PlaneH - 6), 0, 0);

        var barW = LightnessBar.ActualWidth > 4 ? LightnessBar.ActualWidth - 3 : 1;
        LightnessMarker.Margin = new Thickness(Math.Clamp(_l * barW - 1, -1, barW), 0, 0, 0);

        // 亮度条：黑 → 当前色相/饱和度下的纯色 → 白
        var stops = new GradientStopCollection();
        const int seg = 8;
        for (var i = 0; i <= seg; i++)
            stops.Add(new GradientStop(ColorMath.FromHsl(_h, _s, i / (double)seg), i / (double)seg));
        LightnessFill.Fill = new LinearGradientBrush(stops, 0.0);
    }
}
