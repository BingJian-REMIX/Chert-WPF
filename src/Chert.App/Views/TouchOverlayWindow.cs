using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
    // 跟随游戏窗口（problem3）：低频轮询游戏窗口矩形，变化时把面板贴到左/右边缘。
    // 用 DispatcherTimer 而非 WinEventHook —— 钩子要装在游戏进程上，进程重启就得重装，
    // 而面板本身就是随游戏显示/关闭的一次性窗口，轮询足够且实现简单可靠。
    private readonly DispatcherTimer _followTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _dragging;
    private bool _dragMoved;
    private Point _dragStart;
    private Canvas _canvas = new();
    /// <summary>
    /// 切换键（<see cref="TouchButtonKind.Toggle"/>）处于「锁住按住」状态时的释放钩子。
    /// 面板重建或关闭前必须全部执行一遍，否则游戏里会留下一个永远按住的 Shift。
    /// </summary>
    private readonly List<Action> _touchReleaseHooks = new();

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
        // 折叠面板等同于放弃操作：先把锁住的切换键（Shift 潜行）放掉，再隐藏
        closeBtn.Click += (_, _) => { ReleaseLatchedKeys(); Hide(); };
        var handleGrid = new Grid();
        handleGrid.Children.Add(handleText);
        handleGrid.Children.Add(closeBtn);
        handle.Child = handleGrid;
        Grid.SetRow(handle, 0);
        root.Children.Add(handle);

        _canvas = TouchPanelBuilder.Build(_config, false, PressKey, ReleaseKey, _touchReleaseHooks);
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
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    /// <summary>
    /// 跟随一次游戏窗口：把面板贴到游戏窗口左/右边缘并纵向居中。
    /// 读不到窗口（句柄未就绪 / 已最小化 / 已销毁）时静默跳过 —— 位置留上一次的值，
    /// 下一轮会再试。
    /// </summary>
    private void FollowGameWindowOnce()
    {
        if (!_config.FollowGameWindow) return;
        if (_gameHwnd == IntPtr.Zero) return;
        if (!IsWindowVisible(_gameHwnd)) return;
        if (!GetWindowRect(_gameHwnd, out var r)) return;
        if (r.Width <= 0 || r.Height <= 0) return;

        var margin = Math.Max(0, _config.FollowGameWindowMargin);
        var x = _config.FollowGameWindowLeftSide
            ? r.Left + margin
            : r.Right - Width - margin;
        var y = r.Top + (r.Height - Height) / 2;

        // 位置没变就不写属性，避免无谓的布局与重绘（否则面板会持续闪烁）
        if (Math.Abs(Left - x) < 1 && Math.Abs(Top - y) < 1) return;
        Left = x;
        Top = y;
    }

    private void StartFollowing()
    {
        if (!_config.FollowGameWindow) return;
        _followTimer.Tick -= OnFollowTick;
        _followTimer.Tick += OnFollowTick;
        if (!_followTimer.IsEnabled) _followTimer.Start();
        FollowGameWindowOnce();   // 先立刻贴一次，避免显示在默认位置再跳过去
    }

    private void StopFollowing()
    {
        _followTimer.Tick -= OnFollowTick;
        if (_followTimer.IsEnabled) _followTimer.Stop();
    }

    private void OnFollowTick(object? sender, EventArgs e) => FollowGameWindowOnce();

    /// <summary>
    /// 释放所有仍处于「锁住按住」状态的切换键并清空钩子集合。
    /// 重建画布前、窗口关闭前、以及游戏进程退出前都必须调用。
    /// </summary>
    private void ReleaseLatchedKeys()
    {
        foreach (var hook in _touchReleaseHooks)
        {
            try { hook(); } catch { /* 单个钩子失败不影响其余释放 */ }
        }
        _touchReleaseHooks.Clear();
    }

    private void PressKey(int vk) => GameKeySender.KeyDown(_gameHwnd, vk);

    private void ReleaseKey(int vk) => GameKeySender.KeyUp(_gameHwnd, vk);

    private void LoadPosition()
    {
        // 跟随模式：位置由 FollowGameWindow 实时决定，不读也不写记忆坐标
        // （两者同时生效会互相打架 —— 用户拖动面板后下一帧就被拉回边缘）。
        if (_config.FollowGameWindow) return;

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
        // 跟随模式下位置是算出来的，持久化没意义（下次跟随会覆盖），反而留下脏数据
        if (_config.FollowGameWindow) return;
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
            // 句柄就绪后才开始跟随：首轮 GetWindowRect 拿不到窗口会静默跳过，
            // 但为省一次无效轮询，等句柄有了再启定时器。
            Dispatcher.BeginInvoke(new Action(StartFollowing));
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
                // 关总开关会隐藏面板：先把锁住的切换键放掉，别把 Shift 留在游戏里
                if (!cfg.Enabled) { inst.ReleaseLatchedKeys(); inst.Hide(); return; }
                inst.Width = cfg.PanelWidth;
                inst.Height = cfg.PanelHeight + TouchPanelBuilder.HandleHeight;
                inst.Opacity = Clamp(cfg.Opacity, 0.2, 1);
                // 先放出锁住的切换键：旧画布连同它的事件处理器即将被丢弃，
                // 不释放的话 corresponding keyup 就永远发不出去了。
                inst.ReleaseLatchedKeys();
                inst._canvas = TouchPanelBuilder.Build(cfg, false, inst.PressKey, inst.ReleaseKey, inst._touchReleaseHooks);
                Grid.SetRow(inst._canvas, 1);
                if (inst.Content is Grid g && g.Children.Count > 1) g.Children[1] = inst._canvas;
                inst.LoadPosition();
                // 跟随开关可能刚被切换：关 -> 停表（位置交还用户拖动），
                // 开 -> 启表并立刻贴一次边（否则要等下一个轮询周期才生效）。
                if (cfg.FollowGameWindow) inst.StartFollowing();
                else inst.StopFollowing();
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
        // 锁住的切换键必须显式松开：这条 keyup 不会随窗口关闭自动发出，
        // 否则用户会发现游戏里的 Shift 卡住不放（一直潜行）。
        ReleaseLatchedKeys();
        base.OnClosed(e);
        // 停掉跟随定时器：否则它会继续 Tick 一个已关闭的窗口（泄漏 + 无谓空转）
        StopFollowing();
        if (ReferenceEquals(Instance, this)) Instance = null;
    }
}
