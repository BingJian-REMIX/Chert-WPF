using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Chert.App.Services;
using Chert.Core.Input;
using Chert.Core.Profiles;
using Chert.Core.Utils;

namespace Chert.App.Views;

/// <summary>
/// 清单 #11：触屏模式虚拟按键面板。开启后与 HUD 一致——随游戏进程显示、游戏退出即关闭。
/// 默认提供虚拟方向键（W / A / S / D）与跳跃键（Space），其余按键可在布局编辑器中增删并拖动排版。
/// </summary>
public class TouchOverlayWindow : Window
{
    public static TouchOverlayWindow? Instance { get; private set; }

    private TouchControlConfig _config = TouchControlConfig.CreateDefault();
    private Process? _gameProcess;
    private IntPtr _gameHwnd;
    private bool _dragging;
    private bool _dragMoved;
    private Point _dragStart;
    private Canvas _canvas = new();

    public TouchOverlayWindow()
    {
        _config = ProfileStore.Load(GameConstants.DefaultGameRoot).Touch ?? TouchControlConfig.CreateDefault();

        Title = "Chert 触屏控制";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Colors.Transparent);
        Width = _config.PanelWidth;
        Height = _config.PanelHeight + TouchPanelBuilder.HandleHeight;
        Opacity = Clamp(_config.Opacity, 0.2, 1);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TouchPanelBuilder.HandleHeight) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // 顶部拖拽把手：半透明，双击折叠（隐藏面板但保持进程内可用）
        var handle = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x66, 0x10, 0x10, 0x10)),
            CornerRadius = new CornerRadius(8, 8, 0, 0),
            Cursor = Cursors.SizeAll
        };
        var handleText = new TextBlock
        {
            Text = "触屏控制",
            Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        var closeBtn = new Button
        {
            Content = new Chert.App.Themes.PngIcon { Token = "close", Size = 12 },
            Width = 26,
            Height = 20,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0))
        };
        closeBtn.Click += (_, _) => Hide();
        var handleGrid = new Grid();
        handleGrid.Children.Add(handleText);
        handleGrid.Children.Add(closeBtn);
        handle.Child = handleGrid;
        Grid.SetRow(handle, 0);
        root.Children.Add(handle);

        _canvas = TouchPanelBuilder.Build(_config, false, PressKey, ReleaseKey);
        Grid.SetRow(_canvas, 1);
        root.Children.Add(_canvas);

        Content = root;

        // 把手区域拖动窗口
        handle.MouseLeftButtonDown += (_, e) =>
        {
            _dragging = true;
            _dragMoved = false;
            _dragStart = e.GetPosition(this);
            handle.CaptureMouse();
        };
        handle.MouseMove += (_, e) =>
        {
            if (!_dragging) return;
            var p = e.GetPosition(this);
            Left += p.X - _dragStart.X;
            Top += p.Y - _dragStart.Y;
            _dragMoved = true;
        };
        handle.MouseLeftButtonUp += (_, _) =>
        {
            _dragging = false;
            handle.ReleaseMouseCapture();
            if (_dragMoved) SavePosition();
        };

        Loaded += (_, _) => LoadPosition();
        Closing += (_, _) => SavePosition();
    }

    // 注意：Window 基类已有 KeyDown / KeyUp 事件，这里必须换名，否则成员重名编译报错
    private void PressKey(int vk) => GameKeySender.KeyDown(_gameHwnd, vk);

    private void ReleaseKey(int vk) => GameKeySender.KeyUp(_gameHwnd, vk);

    private void LoadPosition()
    {
        if (_config.Left >= 0 && _config.Top >= 0)
        {
            Left = _config.Left;
            Top = _config.Top;
            return;
        }
        // 默认：屏幕底部居中
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = SystemParameters.PrimaryScreenHeight - Height - 40;
    }

    private void SavePosition()
    {
        try
        {
            _config.Left = (int)Left;
            _config.Top = (int)Top;
            var profile = ProfileStore.Load(GameConstants.DefaultGameRoot);
            profile.Touch = _config;
            ProfileStore.Save(profile);
        }
        catch { /* 非关键 */ }
    }

    /// <summary>挂接游戏进程：显示面板，并在游戏退出时自动关闭。</summary>
    public void AttachGame(Process process)
    {
        if (_gameProcess is { } old) old.Exited -= OnGameExited;
        _gameProcess = process;
        if (process is not null)
        {
            process.EnableRaisingEvents = true;
            process.Exited += OnGameExited;
        }
        // 游戏窗口可能稍后才创建，后台等待句柄（不阻塞界面）
        _ = Task.Run(() =>
        {
            _gameHwnd = GameKeySender.FindGameWindow(process);
        });
        Show();
    }

    private void OnGameExited(object? sender, EventArgs e)
    {
        try { Dispatcher.Invoke(() => Close()); } catch { }
    }

    /// <summary>设置改动后即时套用到已打开的面板。</summary>
    public static void ApplyConfig(TouchControlConfig cfg)
    {
        try
        {
            var profile = ProfileStore.Load(GameConstants.DefaultGameRoot);
            profile.Touch = cfg;
            ProfileStore.Save(profile);
        }
        catch { /* 非关键 */ }

        var inst = Instance;
        if (inst is null) return;
        try
        {
            inst.Dispatcher.Invoke(() =>
            {
                inst._config = cfg;
                if (!cfg.Enabled) { inst.Hide(); return; }
                inst.Width = cfg.PanelWidth;
                inst.Height = cfg.PanelHeight + TouchPanelBuilder.HandleHeight;
                inst.Opacity = Clamp(cfg.Opacity, 0.2, 1);
                inst._canvas = TouchPanelBuilder.Build(cfg, false, inst.PressKey, inst.ReleaseKey);
                Grid.SetRow(inst._canvas, 1);
                if (inst.Content is Grid g && g.Children.Count > 1) g.Children[1] = inst._canvas;
                inst.LoadPosition();
                if (inst._gameProcess is not null && !inst.IsVisible) inst.Show();
            });
        }
        catch { /* 非关键 */ }
    }

    /// <summary>游戏进程启动后调用（与 HUD 同一时机）。</summary>
    public static void TryShow(Process gameProcess)
    {
        try
        {
            var profile = ProfileStore.Load(GameConstants.DefaultGameRoot);
            if (!(profile.Touch?.Enabled ?? false)) return;
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Instance is null || !Instance.IsLoaded)
                {
                    Instance = new TouchOverlayWindow { Owner = Application.Current.MainWindow };
                }
                Instance.AttachGame(gameProcess);
            });
        }
        catch { /* 触屏面板属增强功能，失败不影响游戏 */ }
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (ReferenceEquals(Instance, this)) Instance = null;
    }
}
