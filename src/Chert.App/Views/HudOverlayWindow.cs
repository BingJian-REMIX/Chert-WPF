using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Chert.App.Services;
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
    /// <summary>由 GameLauncher 的游戏进程输出回调喂入 HUD 日志行（配套 Mod 输出 [MCLCS-HUD] 时回填 FPS/坐标等）。</summary>
    public static void FeedGameLogLine(string line)
    {
        try { Instance?._provider.TryConsumeLogLine(line); } catch { }
    }

    private void OnGameExited(object? sender, EventArgs e)
    {
        try { Dispatcher.Invoke(() => Close()); } catch { }
    }
    private readonly DispatcherTimer _timer;
    private readonly HudMetricsProvider _provider = new();
    private readonly TextBlock _text;
    private Process? _gameProcess;
    /// <summary>游戏窗口句柄（进程启动后异步获取，用于把 HUD 绑到游戏窗口上）。</summary>
    private IntPtr _gameHwnd;
    private long _maxMemoryMb;
    private HudConfig _config = new();
    private bool _isDragging;
    private bool _dragMoved;
    private Point _dragStart;

    public HudOverlayWindow()
    {
        _config = ProfileStore.Load(GameConstants.DefaultGameRoot).Hud;

        Title = "燧石 HUD";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        // 清单 #54：窗口随内容自适应（此前固定 240×180：字段多选会被裁切、少选则留大片空白）
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 96;
        MinHeight = 24;
        Background = _config.ShowBackground
            ? new SolidColorBrush(Color.FromArgb(0x80, 0x10, 0x10, 0x10))
            : new SolidColorBrush(Colors.Transparent);
        Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = new FontFamily("Consolas");
        FontSize = _config.FontSize;
        // 清单 #54：不透明度此前只存不用，这里落到窗口
        Opacity = _config.Opacity;

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
            _dragMoved = true;
        };
        MouseLeftButtonUp += (_, _) =>
        {
            _isDragging = false;
            ReleaseMouseCapture();
            // 清单 #54：手动拖过之后记为「自定义位置」并落盘，下次启动保持玩家摆好的位置
            if (_dragMoved)
            {
                _dragMoved = false;
                _config.Anchor = HudAnchor.Custom;
                _config.X = (int)Left;
                _config.Y = (int)Top;
                SavePosition();
            }
        };

        Loaded += OnLoaded;
        Closing += (_, _) => SavePosition();
        // HUD 是跟着一局游戏走的：关窗必须停表并把静态实例放开。
        // 留着旧实例会串出两个毛病 —— 定时器继续空转采样一个已退出的进程；
        // 下一局 TryShow 拿到这个已关闭的窗口去 Show，直接抛 InvalidOperationException。
        Closed += (_, _) => ShutdownOverlay();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_config.RefreshMs) };
        _timer.Tick += OnTick;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 应用点击穿透（WS_EX_TRANSPARENT + WS_EX_LAYERED）：清单 #54 起受配置开关控制
        ApplyClickThrough(_config.ClickThrough);

        // 恢复上次位置
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) LoadPosition();

        _timer.Start();
    }

    /// <summary>关窗收尾：停采样定时器、摘掉游戏进程的退出事件、把静态槽位让出来。</summary>
    private void ShutdownOverlay()
    {
        try { _timer.Stop(); } catch { /* 停表失败不该影响关窗 */ }
        if (_gameProcess is { } proc)
        {
            try { proc.Exited -= OnGameExited; } catch { }
            _gameProcess = null;
        }
        if (ReferenceEquals(Instance, this)) Instance = null;
    }

    public void AttachGame(Process process, long maxMemoryMb)
    {
        if (_gameProcess is { } oldProc)
        {
            oldProc.Exited -= OnGameExited;
        }
        _gameProcess = process;
        _maxMemoryMb = maxMemoryMb;
        _provider.SessionStart = DateTime.Now;
        if (_gameProcess is not null)
        {
            _gameProcess.EnableRaisingEvents = true;
            _gameProcess.Exited += OnGameExited;
        }
        _timer.Start();
        Show();
        Activate();
        // 游戏窗口稍后才创建，后台等待句柄后把 HUD 绑到游戏窗口上（不阻塞界面）
        _ = Task.Run(() =>
        {
            _gameHwnd = GameKeySender.FindGameWindow(process);
            if (_gameHwnd != IntPtr.Zero)
                Dispatcher.BeginInvoke(new Action(AttachToGameWindow));
        });
    }
    /// <summary>
    /// 把覆盖层的原生宿主窗口设为<b>游戏窗口</b>。
    /// <para>
    /// 此前覆盖层把 WPF 的 Owner 设成了启动器主窗口，于是点击覆盖层时 Windows
    /// 会把 owner 链一起带到前台 —— 启动器被置顶、游戏反而失去焦点。
    /// </para>
    /// <para>
    /// 这里用 <see cref="WindowInteropHelper.Owner"/>（原生句柄）而不是 WPF 的
    /// <see cref="Window.Owner"/>：后者要求一个 WPF Window 对象，并会连带启用
    /// 「最小化 / 关闭联动」语义，而这里只需要原生 owner 关系 ——
    /// 覆盖层 Z 序跟随游戏窗口，且点击覆盖层时系统激活的是游戏而非启动器。
    /// </para>
    /// </summary>
    private void AttachToGameWindow()
    {
        if (_gameHwnd == IntPtr.Zero) return;
        try { new WindowInteropHelper(this).Owner = _gameHwnd; }
        catch { /* 绑定失败不影响覆盖层使用，退回普通 Topmost 行为 */ }
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
                // 不设 WPF 的 Owner：HUD 是游戏覆盖层，挂到启动器主窗口名下会让点击
                // HUD 时把启动器带到前台。改为绑定游戏窗口（见 AttachGame）。
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
            if (_config.Anchor == HudAnchor.Custom)
            {
                if (_config.X > 0 || _config.Y > 0)
                {
                    Left = _config.X;
                    Top = _config.Y;
                }
                return;
            }

            // 清单 #54：按锚点计算（此前 ComputePosition 已有但从未被调用，拖动之外的位置设置完全没生效）
            var (x, y) = _config.ComputePosition(
                (int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight,
                (int)ActualWidth, (int)ActualHeight);
            Left = x;
            Top = y;
        }
        catch { /* ignore */ }
    }

    /// <summary>清单 #54：设置页改动后即时套用到已打开的 HUD。</summary>
    public void ApplyConfig(HudConfig cfg)
    {
        if (cfg is not null) _config = cfg;
        try
        {
            Dispatcher.Invoke(() =>
            {
                // 总开关关闭时立即隐藏；重新打开且已挂接到游戏进程时再显示
                if (!_config.Enabled) { Hide(); return; }
                if (_gameProcess is not null && !IsVisible) Show();

                FontSize = _config.FontSize;
                Opacity = _config.Opacity;
                Background = _config.ShowBackground
                    ? new SolidColorBrush(Color.FromArgb(0x80, 0x10, 0x10, 0x10))
                    : new SolidColorBrush(Colors.Transparent);
                _timer.Interval = TimeSpan.FromMilliseconds(_config.RefreshMs);
                ApplyClickThrough(_config.ClickThrough);
                LoadPosition();
                _text.Text = HudMetricsProvider.Render(_provider.Sample(_gameProcess, _maxMemoryMb), _config);
            });
        }
        catch { /* 非关键 */ }
    }

    /// <summary>清单 #54：设置页保存 HUD 配置后调用；未打开 HUD 时也会写回配置档，下次启动生效。</summary>
    public static void ApplyConfigFromSettings(HudConfig cfg)
    {
        try
        {
            var profile = ProfileStore.Load(GameConstants.DefaultGameRoot);
            profile.Hud = cfg;
            ProfileStore.Save(profile);
            Instance?.ApplyConfig(cfg);
        }
        catch { /* 非关键 */ }
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

    /// <summary>清单 #54：点击穿透可开关（关闭时移除 WS_EX_TRANSPARENT，保留 WS_EX_LAYERED 以维持透明窗口）。</summary>
    private void ApplyClickThrough(bool enable)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            const int WS_EX_TRANSPARENT = 0x00000020;
            const int WS_EX_LAYERED = 0x00080000;
            const int GWL_EXSTYLE = -20;

            var exStyle = NativeMethods.GetWindowLong(hwnd, GWL_EXSTYLE);
            exStyle = enable ? exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED : exStyle & ~WS_EX_TRANSPARENT;
            NativeMethods.SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
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
