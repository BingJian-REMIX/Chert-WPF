using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Chert.App.Controls;

/// <summary>
/// 可旋转的 Minecraft 皮肤 3D 预览控件：Viewport3D + 鼠标拖拽旋转 + 滚轮缩放 + 空闲慢转。
/// 通过 <see cref="SkinImage"/> 与 <see cref="Slim"/> 依赖属性驱动模型重建。
/// </summary>
public class SkinPreview3D : UserControl
{
    public static readonly DependencyProperty SkinImageProperty =
        DependencyProperty.Register(nameof(SkinImage), typeof(BitmapSource), typeof(SkinPreview3D),
            new PropertyMetadata(null, OnSkinChanged));

    public static readonly DependencyProperty SlimProperty =
        DependencyProperty.Register(nameof(Slim), typeof(bool), typeof(SkinPreview3D),
            new PropertyMetadata(false, OnSkinChanged));

    public static readonly DependencyProperty AutoRotateProperty =
        DependencyProperty.Register(nameof(AutoRotate), typeof(bool), typeof(SkinPreview3D),
            new PropertyMetadata(true, OnAutoRotateChanged));

    public BitmapSource? SkinImage
    {
        get => (BitmapSource?)GetValue(SkinImageProperty);
        set => SetValue(SkinImageProperty, value);
    }

    public bool Slim
    {
        get => (bool)GetValue(SlimProperty);
        set => SetValue(SlimProperty, value);
    }

    public bool AutoRotate
    {
        get => (bool)GetValue(AutoRotateProperty);
        set => SetValue(AutoRotateProperty, value);
    }

    private readonly Viewport3D _viewport = new();
    private readonly ModelVisual3D _root = new();          // 绕 Y 轴（偏航）
    private readonly ModelVisual3D _pitchVisual = new();    // 绕 X 轴（俯仰）
    private readonly Model3DGroup _body = new();            // 角色本体
    private readonly PerspectiveCamera _camera = new();

    private readonly AxisAngleRotation3D _yaw = new(new Vector3D(0, 1, 0), 35);
    private readonly AxisAngleRotation3D _pitch = new(new Vector3D(1, 0, 0), -8);

    private readonly DispatcherTimer _timer = new();
    private Point _lastPoint;
    private bool _dragging;

    public SkinPreview3D()
    {
        // 旋转链：_root(偏航) → _pitchVisual(俯仰) → _body(角色本体)。
        // 注意 ModelVisual3D.Content 只接受 Model3D，嵌套的 Visual3D 必须放进 Children。
        _root.Transform = new RotateTransform3D(_yaw);
        _pitchVisual.Transform = new RotateTransform3D(_pitch);
        _pitchVisual.Content = _body;
        _root.Children.Add(_pitchVisual);
        _viewport.Children.Add(_root);

        // 静态灯光（不随模型旋转）。
        // 用全强度环境光 + 去掉方向光：像素皮肤是平面色块，方向光会把背光的面压暗成「黑影」，
        // 与用户看到的黑边/阴影一致。全环境光保证每个面的像素颜色如实显示、无明暗渐变。
        var lights = new ModelVisual3D
        {
            Content = new Model3DGroup
            {
                Children =
                {
                    new AmbientLight(Colors.White)
                }
            }
        };
        _viewport.Children.Add(lights);

        _camera.Position = new Point3D(0, 0, 58);
        _camera.LookDirection = new Vector3D(0, 0, -1);
        _camera.UpDirection = new Vector3D(0, 1, 0);
        _camera.FieldOfView = 35;
        _viewport.Camera = _camera;

        // 像素艺术皮肤：强制整个 3D 视图走最近邻采样，避免 GPU 把 8×8 面纹理线性插值成糊。
        // 必须设在 Viewport3D（承载 3D 的视觉元素）上；设在笔刷/位图上对 3D 管线无效。
        RenderOptions.SetBitmapScalingMode(_viewport, BitmapScalingMode.NearestNeighbor);

        // 禁用子像素对齐/抗锯齿，让 3D 视图输出按整像素对齐，减少因放大/缩放导致的模糊。
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        RenderOptions.SetEdgeMode(_viewport, EdgeMode.Aliased);

        // Viewport3D 自身无 Background 属性（它不是 Control）；背景由承载的 UserControl 提供。
        // 同时必须给出非 null 背景，否则控件区域命中测试失效，鼠标拖拽旋转收不到事件。
        Background = Brushes.Transparent;
        Content = _viewport;

        _timer.Interval = TimeSpan.FromMilliseconds(33);
        _timer.Tick += (_, _) =>
        {
            if (AutoRotate && !_dragging) _yaw.Angle += 0.4;
        };
        _timer.Start();

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseLeave += (_, _) => _dragging = false;
        MouseWheel += OnWheel;

        Loaded += (_, _) => { if (AutoRotate) _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    private static void OnSkinChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SkinPreview3D)d).RebuildModel();

    private static void OnAutoRotateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (SkinPreview3D)d;
        if (ctrl.AutoRotate) ctrl._timer.Start();
        else ctrl._timer.Stop();
    }

    public void RebuildModel()
    {
        _body.Children.Clear();
        var img = SkinImage;
        // WPF 3D/ImageBrush 会冻结/缓存源位图做 GPU 纹理；若把活的 WriteableBitmap
        // 传进去，它会被冻住，导致皮肤编辑器后续 WritePixels 抛“IsFrozen”异常。
        // 解决：克隆一份并冻结克隆体，让原始 WriteableBitmap 保持可写。
        if (img is WriteableBitmap wb)
        {
            var clone = new WriteableBitmap(wb);
            if (clone.CanFreeze) clone.Freeze();
            img = clone;
        }
        if (img is not null)
            _body.Children.Add(SkinModel3D.Build(img, Slim));
    }

    private void OnMouseDown(object? sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        _dragging = true;
        _lastPoint = e.GetPosition(this);
        CaptureMouse();
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(this);
        var dx = p.X - _lastPoint.X;
        var dy = p.Y - _lastPoint.Y;
        _lastPoint = p;
        _yaw.Angle += dx * 0.5;
        _pitch.Angle = Math.Clamp(_pitch.Angle + dy * 0.5, -80, 80);
    }

    private void OnMouseUp(object? sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private void OnWheel(object? sender, MouseWheelEventArgs e)
    {
        var z = _camera.Position.Z - e.Delta * 0.05;
        z = Math.Clamp(z, 32, 90);
        _camera.Position = new Point3D(_camera.Position.X, _camera.Position.Y, z);
    }
}
