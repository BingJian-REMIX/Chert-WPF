using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Chert.Core.Hud;
using Chert.Core.Profiles;
using Chert.Core.Utils;

namespace Chert.App.Views;

/// <summary>
/// HUD 叠加窗口（规格 3.9）：非侵入独立窗口，点击穿透，跟随游戏窗口，
/// 显示 FPS/内存/CPU/延迟/坐标等实时指标。数据读取失败静默处理。
/// </summary>
public class HudOverlayWindow : Window
{
    public static HudOverlayWindow? Instance { get; private set; }
    private readonly DispatcherTimer _timer;
    private readonly HudMetricsProvider _provider = new();
    private readonly TextBlock _text;
    private Process? _gameProcess;
    private long _maxMemoryMb;
    private HudConfig _config = new();
    private bool _isDragging;
    private Point _dragStart;

    public HudOverlayWindow()
    {
        _config = ProfileStore.Load(GameConstants.DefaultGameRoot).Hud;

        Title = "MCLCS HUD";
        Width = 240;
        Height = 180;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(0x80, 0x10, 0x10, 0x10));
        Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = new FontFamily("Consolas");
        FontSize = _config.FontSize;

        _text = new TextBlock
        {
            Margin = new Thickness(8),
            Text = "等待游戏启动…"
        };
        Content = _text;

        MouseLeftButtonDown += (_, e) =>
        {
            _isDragging = true;
            _dragStart = e.GetPosition(this);
            CaptureMouse();
        };
        MouseMove += (_, e) =>
        {
            if (!_isDragging) return;
            var pos = e.GetPosition(this);
            Left += (pos.X - _dragStart.X);
            Top += (pos.Y - _dragStart.Y);
        };
        MouseLeftButtonUp += (_, _) => { _isDragging = false; ReleaseMouseCapture(); };

        Loaded += OnLoaded;
        Closing += (_, _) => SavePosition();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_config.RefreshMs) };
        _timer.Tick += OnTick;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 应用点击穿透（WS_EX_TRANSPARENT + WS_EX_LAYERED）
        ApplyClickThrough();

        // 恢复上次位置
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) LoadPosition();

        _timer.Start();
    }

    public void AttachGame(Process process, long maxMemoryMb)
    {
        _gameProcess = process;
        _maxMemoryMb = maxMemoryMb;
        _provider.SessionStart = DateTime.Now;
        _timer.Start();
        Show();
        Activate();
    }

    /// <summary>检查设置并激活 HUD（仅在 Hud.Enabled 且 Instance 已创建时调用）。</summary>
    public static void TryShow(Process gameProcess, long maxMemoryMb = 0)
    {
        var profile = ProfileStore.Load(GameConstants.DefaultGameRoot);
        if (!profile.Hud.Enabled) return;
        if (Instance is null)
        {
            Instance = new HudOverlayWindow();
            Application.Current.Dispatcher.Invoke(() =>
            {
                Instance.Owner = Application.Current.MainWindow;
                Instance.Show();
            });
        }
        Instance.Dispatcher.Invoke(() => Instance.AttachGame(gameProcess, maxMemoryMb));
    }

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            // 仅当游戏前台时显示：开启该选项且游戏不在前台则给出占位提示，避免 HUD 看起来"空"。
            if (_gameProcess is { HasExited: false } && _config.OnlyWhenGameForeground && !IsGameForeground(_gameProcess))
            {
                Dispatcher.Invoke(() => _text.Text = "（游戏未在前台）");
                return;
            }
            var metrics = _provider.Sample(_gameProcess, _maxMemoryMb);
            Dispatcher.Invoke(() => _text.Text = HudMetricsProvider.Render(metrics, _config));
        }
        catch
        {
            // 静默处理
        }
    }

    /// <summary>判断游戏进程是否拥有当前前台窗口。</summary>
    private static bool IsGameForeground(Process game)
    {
        try
        {
            var fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            NativeMethods.GetWindowThreadProcessId(fg, out uint pid);
            return pid == game.Id;
        }
        catch
        {
            return false;
        }
    }

    private void LoadPosition()
    {
        try
        {
            if (_config.X > 0 || _config.Y > 0)
            {
                Left = _config.X;
                Top = _config.Y;
            }
        }
        catch { /* ignore */ }
    }

    private void SavePosition()
    {
        try
        {
            _config.X = (int)Left;
            _config.Y = (int)Top;
            var profile = ProfileStore.Load(GameConstants.DefaultGameRoot);
            profile.Hud = _config;
            ProfileStore.Save(profile);
        }
        catch { /* ignore */ }
    }

    private void ApplyClickThrough()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            const int WS_EX_TRANSPARENT = 0x00000020;
            const int WS_EX_LAYERED = 0x00080000;
            const int GWL_EXSTYLE = -20;

            var exStyle = NativeMethods.GetWindowLong(hwnd, GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        }
        catch { /* non-critical */ }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
