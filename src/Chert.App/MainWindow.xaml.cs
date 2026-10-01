using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Chert.Core.Localization;
using Chert.Core.Profiles;
using Chert.Core.Theme;
using Chert.Core.UI;
using Chert.Core.Utils;
using Chert.App.Services;
using Chert.App.Themes;
using Chert.App.ViewModels;
using Chert.App.Views;
using System.Windows.Shapes;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace Chert.App;

public partial class MainWindow : Window
{
    // bug #12：Win11 圆角。通过 DWM 让系统把整个窗口位图圆角化（需 Windows 11）。
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWM_WINDOW_CORNER_PREFERENCE_ROUND = 2;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    private void EnableWin11Corners()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int pref = DWM_WINDOW_CORNER_PREFERENCE_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch { /* 非 Win11 或失败则忽略，保持直角 */ }
    }

    // ===== 清单 #16：玻璃风格 —— 系统级毛玻璃背板 =====
    // Win11 22H2+：DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE) 真实亚克力背景模糊；
    // Win10 1803+：SetWindowCompositionAttribute(ACCENT_ENABLE_ACRYLICBLURBEHIND) 降级亚克力。
    // 两者皆不可用时返回 false，调用方落回不透底的静态玻璃底，避免半透明层叠在无背板的黑底上发灰。
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_NONE = 1;
    private const int DWMSBT_TRANSIENTWINDOW = 3;   // 亚克力（Acrylic）

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;   // ABGR
        public int AnimationId;
    }

    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    /// <summary>当前是否已开启系统级背板（用于切出玻璃风格时关闭）。</summary>
    private bool _backdropActive;

    /// <summary>清单 #16：玻璃风格的等宽字体（终端观感），带回退字族。</summary>
    private static readonly FontFamily MonoFont = new("Consolas, Menlo, Courier New");

    /// <summary>清单 #16：玻璃浏览器式页签的最小宽度（对齐 HTML .window.minimal .tab 的 min-width:96px）。</summary>
    private const double GlassTabMinWidth = 96;

    // 清单 #16：外壳原始值快照（构造时记录，风格切出时逐项还原）
    private GridLength _titleBarRowHeight;
    private GridLength _statusBarRowHeight;
    private string _brandTextDefault = "";
    private FontFamily _brandFontDefault = null!;
    private double _brandFontSizeDefault;
    private FontFamily _statusFontDefault = null!;
    private double _statusFontSizeDefault;

    /// <summary>尝试开启系统级毛玻璃背板；成功返回 true（调用方据此决定窗体底色用半透明还是实色）。</summary>
    private bool TryEnableBackdrop()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return false;

            // 系统绘制的窗框 / 标题栏按钮跟随亮暗主题
            int useDark = ThemeManager.Current == ThemeType.Dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));

            // Win11 22H2+：系统亚克力背板（返回 S_OK 才算成功）
            if (!AllowsTransparency)
            {
                int type = DWMSBT_TRANSIENTWINDOW;
                if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int)) == 0)
                {
                    _backdropActive = true;
                    return true;
                }
            }

            // Win10 1803+：ACCENT_ENABLE_ACRYLICBLURBEHIND 降级
            var accent = new AccentPolicy
            {
                AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 2,
                GradientColor = unchecked((int)0x99000000)   // ABGR：A=0x99
            };
            var size = Marshal.SizeOf<AccentPolicy>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = ptr,
                    SizeOfData = size
                };
                _backdropActive = SetWindowCompositionAttribute(hwnd, ref data) != 0;
                return _backdropActive;
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        catch { return false; }
    }

    /// <summary>关闭系统级背板（切出玻璃风格时），恢复普通不透底窗口。</summary>
    private void DisableBackdrop()
    {
        _backdropActive = false;
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int none = DWMSBT_NONE;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
            var accent = new AccentPolicy { AccentState = 0, AccentFlags = 0, GradientColor = 0 };
            var size = Marshal.SizeOf<AccentPolicy>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = ptr,
                    SizeOfData = size
                };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        catch { /* 忽略：关闭失败不影响功能 */ }
    }

    // ===== 最大化：限制到当前工作区，避免覆盖任务栏 =====
    private const int WM_GETMINMAXINFO = 0x0024;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    private void AttachMaximizeHook()
    {
        var helper = new WindowInteropHelper(this);
        if (helper.Handle == IntPtr.Zero) return;
        var source = HwndSource.FromHwnd(helper.Handle);
        source?.AddHook(WndProcHook);
    }

    private static IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            try
            {
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                var source = HwndSource.FromHwnd(hwnd);
                if (source?.CompositionTarget != null)
                {
                    var t = source.CompositionTarget.TransformToDevice;
                    var work = SystemParameters.WorkArea;
                    mmi.ptMaxSize.x = (int)(work.Width * t.M11);
                    mmi.ptMaxSize.y = (int)(work.Height * t.M22);
                    mmi.ptMaxPosition.x = (int)(work.Left * t.M11);
                    mmi.ptMaxPosition.y = (int)(work.Top * t.M22);
                    Marshal.StructureToPtr(mmi, lParam, false);
                    handled = true;
                }
            }
            catch { /* 失败则回退到默认最大化行为 */ }
        }
        return IntPtr.Zero;
    }

    private void BtnMax_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        RefreshMaximizeIcon();
    }

    private void RefreshMaximizeIcon()
    {
        var isMax = WindowState == WindowState.Maximized;
        if (MaxIcon != null) MaxIcon.Visibility = isMax ? Visibility.Collapsed : Visibility.Visible;
        if (RestoreIcon != null) RestoreIcon.Visibility = isMax ? Visibility.Visible : Visibility.Collapsed;
    }

    // 主标签 → 页面
    private readonly Dictionary<MainTabKind, FrameworkElement> _pages = new();
    private MainTabKind _currentKind = (MainTabKind)(-1);
    /// <summary>
    /// 当前打开的「版本大页」（版本库 / 版本设置）。版本大页属于游戏页的子导航，
    /// 切到其它大页时隐藏、回到游戏页时恢复（bug3.txt #3）。
    /// </summary>
    private FrameworkElement? _gameBigPage;
    /// <summary>
    /// 页面切换动画开关。设置页修改后需即时生效（此前只在窗口构造时读一次，必须重启才生效）。
    /// </summary>
    public static bool AnimationsEnabled { get; set; } = true;

    // 索引贴可视部件
    private readonly Dictionary<MainTabKind, TabParts> _tabs = new();
    // 清单 #15：沉底导航可视部件（简约安卓式风格启用）
    private readonly Dictionary<MainTabKind, BottomNavParts> _bottomNav = new();
    // 清单 #15：安卓顶部横向子标签可视部件（副页 Id → 胶囊）
    private readonly Dictionary<string, TopTabParts> _topTabs = new();
    // 清单 #34：彩蛋触发序列（↑↑↓↓←→←→BA）匹配进度
    private static readonly Key[] KonamiSequence =
    {
        Key.Up, Key.Up, Key.Down, Key.Down, Key.Left, Key.Right, Key.Left, Key.Right, Key.B, Key.A
    };
    private int _konamiIndex;
    // 侧边栏可视部件
    private readonly Dictionary<string, SidebarParts> _sidebarItems = new();
    private readonly SidebarState _sidebarState = new();

    private DispatcherTimer? _expandTimer;
    private DispatcherTimer? _collapseTimer;
    private TrayIconService? _tray;

    public MainWindow()
    {
        InitializeComponent();

        // 清单 #16：快照外壳默认值（标题栏/状态栏行高、品牌文案、状态栏字体），供玻璃风格切出时还原
        _titleBarRowHeight = TitleBarRow.Height;
        _statusBarRowHeight = StatusBarRow.Height;
        _brandTextDefault = BrandText.Text;
        _brandFontDefault = BrandText.FontFamily;
        _brandFontSizeDefault = BrandText.FontSize;
        _statusFontDefault = StatusBarRoot.FontFamily;
        _statusFontSizeDefault = StatusBarRoot.FontSize;

        _pages[MainTabKind.Game] = new GameView();
        _pages[MainTabKind.Download] = new DownloadPageView();
        _pages[MainTabKind.Toolbox] = new ToolboxView();
        _pages[MainTabKind.Settings] = new SettingsView();

        // bug #14：标题栏下载入口与弹窗共享下载页 VM 的同一份队列。
        // 不能在 XAML 里用 x:Static —— InitializeComponent 阶段 Current 尚为 null。
        if (DownloadPageViewModel.Current is { } dlVm)
        {
            DownloadPopupBtn.DataContext = dlVm;
            DownloadQueuePopup.DataContext = dlVm;
        }

        BuildTabs();
        ApplyTabLayout(MainTabKind.Game);
        SetTabTheme(MainTabKind.Game);

        // 清单 #15：界面风格（安卓式 → 沉底导航）。风格字典由 App 层叠加，这里只管导航形态。
        ThemeManager.OnUiStyleChanged += _ => Dispatcher.Invoke(ApplyUiStyle);
        // 清单 #16：亮暗主题切换后重新套用外壳（玻璃/安卓的外壳配色取的是当次资源快照）
        ThemeManager.OnThemeChanged += _ => Dispatcher.Invoke(ApplyUiStyle);
        ApplyUiStyle();

        _sidebarState.SwitchOwner(MainTabKind.Game);
        BuildSidebar(MainTabKind.Game);

        // 四色索引贴入场动画在窗口 Loaded 后播一次（首次揭示效果，错峰滑入 + 淡入）
        Loaded += (_, _) => PlayTabEntrance();
        // bug #12：Win11 圆角
        Loaded += (_, _) => EnableWin11Corners();
        // bug #86：最大化按钮 + 限制到工作区（不覆盖任务栏）
        SourceInitialized += (_, _) => AttachMaximizeHook();
        // 清单 #16：窗口句柄就绪后再尝试开启毛玻璃背板（构造期 Handle 尚未创建）
        SourceInitialized += (_, _) => { if (IsGlassStyle()) ApplyUiStyle(); };
        Loaded += (_, _) => RefreshMaximizeIcon();
        // 窗口尺寸变化时（侧边栏可视高度改变）→ 重新把当前选中项居中
        SizeChanged += (_, _) => RequestSidebarCenter();
        // 清单 #34：隐藏彩蛋——按 ↑↑↓↓←→←→BA 打开愚人节小游戏
        PreviewKeyDown += OnPreviewKeyDown;
        StateChanged += (_, _) => RefreshMaximizeIcon();
        // bug #10：窗口就绪后尝试断点续播（MediaElement 此时已可播放）
        Loaded += (_, _) => MusicPlayerViewModel.Instance.RestoreLastState();
        // 窗口布局记忆：恢复上次的尺寸 / 位置 / 最大化状态
        Loaded += (_, _) => RestoreWindowLayout();

        // 清单 #63：全局快捷键（主窗口句柄就绪后注册系统级热键）
        Loaded += (_, _) => Chert.App.Services.GlobalHotkeyService.Attach(this);

        // 语言切换时刷新主标签与侧边栏标题
        LocaleManager.LocaleChanged += _ => Dispatcher.Invoke(() =>
        {
            BuildTabs();
            ApplyTabLayout(_currentKind);
            SetTabTheme(_currentKind);
            BuildSidebar(_sidebarState.Owner);
            if (!string.IsNullOrEmpty(_sidebarState.SelectedId))
                UpdateSidebarSelection();
        });

        PageHost.Content = _pages[MainTabKind.Game];
        _currentKind = MainTabKind.Game;

        // bug #10：注册版本库大页导航（覆盖内容区）
        BigPageNavigator.ShowHandler = ShowBigPage;
        BigPageNavigator.CloseHandler = CloseBigPage;

        AnimationsEnabled = ProfileStore.Load(GameConstants.DefaultGameRoot).AnimationsEnabled;

        // bug #21（游戏目录切换）：页面在构造期一次性缓存，换目录后版本 / 存档列表仍是旧数据，
        // 故重建除设置页外的三个页面。设置页正是事件源，重建它会销毁当前正在显示的界面。
        Chert.App.Services.LauncherService.GameRootChanged += () => Dispatcher.BeginInvoke(() =>
        {
            try
            {
                _pages[MainTabKind.Game] = new GameView();
                _pages[MainTabKind.Download] = new DownloadPageView();
                _pages[MainTabKind.Toolbox] = new ToolboxView();
                if (_currentKind != MainTabKind.Settings)
                    PageHost.Content = _pages[_currentKind];
            }
            catch { /* 重建失败不影响当前会话 */ }
        });

        // 音乐播放器解码宿主（MediaElement 实现 IMediaPlayer）
        var player = new MediaElementPlayer(MusicMedia);
        MusicPlayerViewModel.Instance.Host = player;
        player.Ended += () => MusicPlayerViewModel.Instance.OnTrackEnded();
        MusicPlayerViewModel.Instance.SetVolumeFromHost(); // 推送初始音量到 MediaElement
        ((System.Windows.Controls.Primitives.Popup)MusicListPopup).Closed += (_, _) => MusicPlayerViewModel.Instance.Expanded = false;

        LauncherService.Instance.Logged += line =>
        {
            if (!string.IsNullOrWhiteSpace(line))
                StatusBarViewModel.Current.LastLog = line;
        };
        Closed += (_, _) => _ = StatusBarViewModel.Current.RefreshAsync();

        // §2.3-16 焦点回归时检测手动丢入的新文件（首次 Activated 只建基线，不弹通知）
        Activated += MainWindow_Activated;

        // bug #26：最小化到托盘。托盘图标常驻，提供「打开主界面 / 退出」；
        // 当 MinimizeToTray 开启时，最小化即隐藏主窗口到系统托盘（见 MainWindow_StateChanged）。
        _tray = new TrayIconService(this, RestoreFromTray, () => Application.Current.Shutdown());
        StateChanged += MainWindow_StateChanged;
        Closing += (_, _) => _tray?.Dispose();
        // 布局保存到 Closing 而非 Closed：Closed 时窗口的部分度量信息已不可读
        Closing += (_, _) => SaveWindowLayout();

        Closing += (_, _) => Chert.App.Services.GlobalHotkeyService.Detach();

        // 启动时自动检查更新（设置项 AutoUpdateCheck，默认开启）：发现新版本则拉取 tag 日志并弹窗。
        // 与「设置页-检查更新」共用 UpdateNotifier，失败静默忽略，不阻塞启动。
        Loaded += (_, _) =>
        {
            try
            {
                if (ProfileStore.Load(GameConstants.DefaultGameRoot).AutoUpdateCheck)
                    _ = UpdateNotifier.CheckAndShowAsync();
            }
            catch
            {
                // 自动检查失败不影响启动
            }
        };
    }

    // ===== 窗口布局记忆（清单 2.6 功能改进） =====

    /// <summary>恢复上次的窗口尺寸 / 位置 / 最大化状态；任一环节失败都退回默认居中布局。</summary>
    private void RestoreWindowLayout()
    {
        try
        {
            var profile = ProfileStore.Load(GameConstants.DefaultGameRoot);

            if (profile.WindowWidth is > 0 && profile.WindowHeight is > 0)
            {
                Width = profile.WindowWidth.Value;
                Height = profile.WindowHeight.Value;
            }

            // 坐标要做越界校验：换显示器 / 改分辨率后，旧坐标可能落在可见区之外
            if (profile.WindowLeft is { } left && profile.WindowTop is { } top
                && IsLocationOnScreen(left, top))
            {
                WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }

            if (profile.WindowMaximized)
                WindowState = System.Windows.WindowState.Maximized;
        }
        catch
        {
            // 布局恢复失败不影响正常使用
        }
    }

    /// <summary>把窗口当前尺寸 / 位置写入 profile。</summary>
    private void SaveWindowLayout()
    {
        try
        {
            var profile = ProfileStore.Load(GameConstants.DefaultGameRoot);
            // 最大化时 Left/Top/Width/Height 是最大化后的实际值，恢复它会得到填满屏幕的窗口，
            // 因此最大化状态下只记录标志，保留上一次的正常态几何。
            profile.WindowMaximized = WindowState == System.Windows.WindowState.Maximized;
            if (!profile.WindowMaximized)
            {
                profile.WindowLeft = Left;
                profile.WindowTop = Top;
                profile.WindowWidth = Width;
                profile.WindowHeight = Height;
            }
            ProfileStore.Save(profile);
        }
        catch
        {
            // 保存失败忽略，下次启动用默认布局
        }
    }

    /// <summary>判断左上角坐标是否落在虚拟屏幕内（留 40px 余量保证标题栏可拖回）。</summary>
    private static bool IsLocationOnScreen(double left, double top)
    {
        var vsLeft = SystemParameters.VirtualScreenLeft;
        var vsTop = SystemParameters.VirtualScreenTop;
        var leftOk = left >= vsLeft - 40
                     && left <= vsLeft + SystemParameters.VirtualScreenWidth - 120;
        var topOk = top >= vsTop - 40
                    && top <= vsTop + SystemParameters.VirtualScreenHeight - 80;
        return leftOk && topOk;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized &&
            ProfileStore.Load(GameConstants.DefaultGameRoot).MinimizeToTray)
        {
            // 最小化到托盘：先移除任务栏按钮再隐藏窗口，避免任务栏残留空白占位。
            ShowInTaskbar = false;
            Visibility = Visibility.Hidden;
        }
    }

    /// <summary>从托盘恢复主窗口（双击托盘图标 / 右键「打开主界面」）。</summary>
    private void RestoreFromTray()
    {
        // 先恢复可见性和任务栏按钮，再解除最小化，避免 DWM 残留最小化状态。
        Visibility = Visibility.Visible;
        ShowInTaskbar = true;
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    private DateTime _lastFileWatch = DateTime.MinValue;

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        // 防抖：同一秒内不重复扫描
        var now = DateTime.Now;
        if ((now - _lastFileWatch).TotalSeconds < 1) return;
        _lastFileWatch = now;
        _ = LaunchCoordinator.CheckFileChangesAsync();

        // 自动同步游戏内添加/删除的服务器（servers.dat）
        if (_pages.TryGetValue(MainTabKind.Game, out var gamePage) && gamePage is GameView gv)
        {
            if (gv.DataContext is GameViewModel gvm)
                gvm.RefreshServers();
        }
    }

    // ===== 索引贴构建（纯文字 + 四色贴，无图标） =====

    private void BuildTabs()
    {
        TabPanel.Children.Clear();
        _tabs.Clear();
        BottomNavPanel.Children.Clear();
        _bottomNav.Clear();

        var last = MainTabs.All.Count - 1;
        for (var i = 0; i < MainTabs.All.Count; i++)
        {
            var def = MainTabs.All[i];
            var grid = new Grid
            {
                Height = MainTabs.TabHeight,
                Width = def.AlwaysExpanded ? MainTabs.ExpandedWidth : MainTabs.CollapsedWidth,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Tag = def
            };

            // 圆角贴纸：外侧（最左/最右）圆角、重叠接缝侧切直，
            // 避免圆角透明区透出下层邻贴造成漏白，同时保留贴纸外观。
            var bg = new Border
            {
                CornerRadius = TabCornerRadius(i, last),
                Background = Brushes.Transparent
            };

            var inner = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Height = MainTabs.TabHeight,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(10, 0, 10, 0)
            };
            var title = new TextBlock
            {
                Text = LocaleManager.T(def.Title),
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            inner.Children.Add(title);

            // bug2.txt #5：选中指示条改为「手机导航栏式」短条——不贯穿、居中、奶白色、胶囊圆角
            var underline = new Rectangle
            {
                Height = MainTabs.UnderlineHeight,
                Width = 22,
                RadiusX = 2,
                RadiusY = 2,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Fill = CreamUnderline,
                Visibility = Visibility.Collapsed
            };

            grid.Children.Add(bg);
            grid.Children.Add(inner);
            grid.Children.Add(underline);

            // 四色索引贴变换：缩放 + 上浮，供悬浮弹跳 / 入场错峰使用（中心为锚点）
            var scale = new ScaleTransform(1, 1);
            var lift = new TranslateTransform(0, 0);
            var tg = new TransformGroup();
            tg.Children.Add(scale);
            tg.Children.Add(lift);
            grid.RenderTransform = tg;
            grid.RenderTransformOrigin = new Point(0.5, 0.5);

            // bug #8：索引贴位于标题栏内，MouseLeftButtonDown 会冒泡触发 TitleBar 的 DragMove()，
            // 拖动模态循环吞掉后续 MouseLeftButtonUp，导致 SelectTab 永不执行（窗口未最大化时尤其明显）。
            // 这里在按下阶段截断冒泡，保证抬起事件能正常派发到索引贴。
            grid.MouseLeftButtonDown += (_, e) => e.Handled = true;
            grid.MouseLeftButtonUp += (_, _) => SelectTab(def.Kind);

            // 鼠标悬浮动画（bug：顶栏漏白修复 + 索引贴悬浮反馈）
            grid.MouseEnter += (_, _) => OnTabHover(def.Kind, true);
            grid.MouseLeave += (_, _) => OnTabHover(def.Kind, false);

            TabPanel.Children.Add(grid);
            Panel.SetZIndex(grid, def.ZIndex);

            _tabs[def.Kind] = new TabParts(grid, bg, title, underline, scale, lift);

            // 清单 #15：沉底导航项（与顶部索引贴同源，安卓式风格时取代顶部索引贴）
            // 清单 #15：宽度交给 UniformGrid 等分（HTML .bnav-item{flex:1}），不再固定 92px
            var navRoot = new Grid
            {
                Margin = new Thickness(2, 0, 2, 0),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent
            };
            var navInner = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            // 清单 #15：对齐 HTML .bnav-item svg{22px} + span{11px;font-weight:600} + gap:4px
            var navIcon = new PngIcon { Token = def.Icon, Size = 22 };
            var navTitle = new TextBlock
            {
                Text = LocaleManager.T(def.Title),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            navInner.Children.Add(navIcon);
            navInner.Children.Add(navTitle);
            // 选中指示：Material 式的「胶囊高亮底」
            var navPill = new Border
            {
                CornerRadius = new CornerRadius(12),
                Height = 32,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = Brushes.Transparent
            };
            navRoot.Children.Add(navPill);
            navRoot.Children.Add(navInner);
            navRoot.MouseLeftButtonUp += (_, _) => SelectTab(def.Kind);

            BottomNavPanel.Children.Add(navRoot);
            _bottomNav[def.Kind] = new BottomNavParts(navRoot, navPill, navTitle, navIcon);
        }
    }

    /// <summary>按当前选中态刷新所有索引贴的宽度 / 配色 / 细线 / 重叠。</summary>
    private void ApplyTabLayout(MainTabKind selected)
    {
        var list = MainTabs.All;
        // 清单 #16：玻璃风格是「浏览器式页签」——全部等宽、都带文字、几乎不重叠，整排靠左排列。
        // 对齐 HTML .window.minimal .tab{width:auto; min-width:96px} 与容器 justify-content:flex-start。
        var glassTabs = IsGlassStyle();
        for (var i = 0; i < list.Count; i++)
        {
            var def = list[i];
            var p = _tabs[def.Kind];
            var isSel = def.Kind == selected;
            var expanded = glassTabs || isSel || def.AlwaysExpanded;
            var w = expanded ? MainTabs.ExpandedWidth : MainTabs.CollapsedWidth;

            if (glassTabs)
            {
                // width:auto —— 宽度按文字自适应（MinWidth 兜底），取消折叠/展开的宽度动画
                p.Root.BeginAnimation(FrameworkElement.WidthProperty, null);
                p.Root.MinWidth = GlassTabMinWidth;
                p.Root.Width = double.NaN;
            }
            else
            {
                p.Root.MinWidth = 0;
                AnimateWidth(p.Root, w);
            }

            // 每贴独立克隆画刷（p.Brush），避免悬浮动画连累合并字典里的共享/冻结画刷。
            // 圆角透明区已由 TabCornerRadius 的「重叠侧切直」消除，Root 保持透明，保留贴纸外观。
            var solid = TabColor($"Tab{def.Kind}Brush");
            p.Brush.Color = isSel ? TabColor($"Tab{def.Kind}ActiveBrush") : solid;
            p.BaseColor = p.Brush.Color;
            // 悬浮/选中提亮到该色 Active 档（#4CAF50→#55C45A 等），与 HTML 的 brightness(1.12) 一致
            p.HoverColor = TabColor($"Tab{def.Kind}ActiveBrush");
            p.Bg.Background = p.Brush;

            p.Title.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            p.Title.Opacity = expanded ? 1 : 0;

            p.Underline.Visibility = isSel ? Visibility.Visible : Visibility.Collapsed;
            if (isSel)
            {
                p.Underline.Fill = CreamUnderline;
                // 选中下划线：opacity 平滑渐显到 1（对齐 HTML 的 .tab.selected .underline{opacity:1}，无呼吸循环）
                p.Underline.Opacity = 0;
                if (AnimationsEnabled)
                    p.Underline.BeginAnimation(Rectangle.OpacityProperty,
                        new DoubleAnimation(1, TimeSpan.FromMilliseconds(MainTabs.TransitionMs)));
                else
                    p.Underline.Opacity = 1;
            }

            // 重叠：左侧邻居展开则 10px，否则 20px；玻璃页签是独立页签（HTML margin-left:-1px），几乎不重叠
            var left = i == 0 ? 0 : (glassTabs ? -1 :
                (NeighborExpanded(i, selected) ? -MainTabs.ExpandedOverlap : -MainTabs.CollapsedOverlap));
            if (glassTabs)
            {
                p.Root.BeginAnimation(FrameworkElement.MarginProperty, null);
                p.Root.Margin = new Thickness(left, 0, 0, 0);
            }
            else
            {
                AnimateMargin(p.Root, new Thickness(left, 0, 0, 0));
            }

            // 选中贴抬到最上层（ZIndex=20），避免被左侧未选中贴的圆角/色块遮住展开后的文字
            Panel.SetZIndex(p.Root, isSel ? 20 : def.ZIndex);

            // 清单 #16：玻璃风格标签走「浏览器式」中性外观；其余风格恢复四色贴纸原貌
            if (IsGlassStyle()) ApplyGlassTabChrome(p, isSel);
            else ApplyDefaultTabChrome(p, i, list.Count);
        }
    }

    private static bool NeighborExpanded(int index, MainTabKind selected) =>
        index > 0 && (MainTabs.All[index - 1].Kind == selected || MainTabs.All[index - 1].AlwaysExpanded);

    // ===== 主题跟着主标签走 =====

    private void SetTabTheme(MainTabKind kind)
    {
        var solid = Brush($"Tab{kind}Brush");
        // 覆写 App 级资源，所有 DynamicResource 引用即时刷新
        Application.Current.Resources["TitleBarBrush"] = solid;
        Application.Current.Resources["SidebarIndicatorBrush"] = solid;
        // 索引贴与对应页面「一体」：页面顶部以同色渲染，消除顶栏后方的白色漏出，
        // 让选中标签的颜色向下延续到内容区（一条渐隐的同色带）。
        ApplyPageTint(kind);
    }

    // ===== 索引贴悬浮 / 配色辅助 =====

    /// <summary>取四色画刷的实色（克隆自合并字典，可安全动画）。</summary>
    private static Color TabColor(string key) =>
        ((SolidColorBrush)Application.Current.FindResource(key)).Color;

    /// <summary>按系数缩放 RGB 亮度（factor&gt;1 提亮，&lt;1 变暗，通道钳制 0-255）。</summary>
    private static Color BrightenColor(Color c, double f)
    {
        static byte S(int v, double ff) => (byte)Math.Clamp((int)Math.Round(v * ff), 0, 255);
        return Color.FromArgb(c.A, S(c.R, f), S(c.G, f), S(c.B, f));
    }

    /// <summary>索引贴悬浮反馈（对齐 HTML 完美版：克制平稳 + 悬浮展开）。
    /// 对齐 倒数第二代.html 的 <c>.tab:not(.expanded):hover{width:130px; filter:brightness(1.12)}</c>：
    /// 悬浮到「未展开」的索引贴时，宽度平滑展开到 ExpandedWidth(130)、文字淡入、亮度提亮到 Active 档、并抬到邻贴之上；
    /// 移出则收回到 CollapsedWidth(56)、文字淡出、还原实色。已选中的贴只做亮度过渡，不改宽度。</summary>
    private void OnTabHover(MainTabKind kind, bool enter)
    {
        if (!_tabs.TryGetValue(kind, out var p) || p.Brush is null) return;
        var def = MainTabs.Get(kind);
        var expandedNow = kind == _currentKind || def.AlwaysExpanded;

        // 悬浮展开（仅对未展开的贴生效）：宽度过渡 width 0.25s ease + 文字淡入淡出。
        // 玻璃是恒定宽度的浏览器式页签，悬停只换底色（HTML .tab:hover 不改宽度），不展开。
        if (!expandedNow && !IsGlassStyle())
        {
            if (AnimationsEnabled)
            {
                p.Root.BeginAnimation(FrameworkElement.WidthProperty,
                    new DoubleAnimation(enter ? MainTabs.ExpandedWidth : MainTabs.CollapsedWidth,
                        TimeSpan.FromMilliseconds(250)));
                RevealTitle(p, enter, true);
            }
            else
            {
                p.Root.Width = enter ? MainTabs.ExpandedWidth : MainTabs.CollapsedWidth;
                RevealTitle(p, enter, false);
            }
            // 悬浮时抬到邻贴之上，保证展开后的文字完整可见（对齐 poker-card 悬浮置顶）；
            // 移出时若仍是当前选中贴则保持置顶，否则回到层叠序
            Panel.SetZIndex(p.Root, enter ? 20 : (def.Kind == _currentKind ? 20 : def.ZIndex));
        }

        // 亮度过渡（对一切贴生效）：进入提亮到 Active 档，移出还原实色
        var target = enter ? p.HoverColor : p.BaseColor;
        if (AnimationsEnabled)
            p.Brush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(target, TimeSpan.FromMilliseconds(MainTabs.HoverMs)));
        else
            p.Brush.Color = target;
    }

    /// <summary>索引贴文字淡入/淡出（对齐 HTML 的 .tab-ico/.tab-txt opacity 过渡）：
    /// 展开时 0→1 淡入，收起时 1→0 淡出后隐藏。关闭动画开关时直接置值。</summary>
    private static void RevealTitle(TabParts p, bool show, bool animate)
    {
        if (show)
        {
            p.Title.Visibility = Visibility.Visible;
            if (animate)
            {
                p.Title.Opacity = 0;
                p.Title.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)));
            }
            else
            {
                p.Title.Opacity = 1;
            }
        }
        else
        {
            if (animate)
            {
                var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
                a.Completed += (_, _) =>
                {
                    if (p.Title.Opacity <= 0.01) p.Title.Visibility = Visibility.Collapsed;
                };
                p.Title.BeginAnimation(UIElement.OpacityProperty, a);
            }
            else
            {
                p.Title.Opacity = 0;
                p.Title.Visibility = Visibility.Collapsed;
            }
        }
    }

    /// <summary>索引贴圆角：仅外侧（最左=左上、最右=右上）保留贴纸圆角，下端一律切直（直角），
    /// 让四色贴像贴在标题栏底部的色块、底部齐平无毛边。</summary>
    private static CornerRadius TabCornerRadius(int index, int last)
    {
        if (index == 0) return new CornerRadius(8, 0, 0, 0);       // 最左：仅左上圆角，下端直角
        if (index == last) return new CornerRadius(0, 8, 0, 0);   // 最右：仅右上圆角，下端直角
        return new CornerRadius(0, 0, 0, 0);                      // 中间：全切直
    }

    /// <summary>页面背景随当前主标签着色：顶部一段实色带与标题栏同色，向下渐隐到窗口底色，
    /// 使「索引贴与对应页面一体」，并消除顶栏后方的白色漏出。</summary>
    private void ApplyPageTint(MainTabKind kind)
    {
        if (PageBorder is null) return;
        var tab = TabColor($"Tab{kind}Brush");
        // 灵动：整页卡片直接用索引贴实色（无渐隐带），与透明标题栏下的彩色大卡片一致
        if (IsDynamicStyle())
        {
            PageBorder.Background = new SolidColorBrush(tab);
            return;
        }
        if (IsAndroidStyle())
        {
            // 安卓：内容区不挂索引贴色带，回归纯窗口底（对齐 HTML .page-tint{display:none}）
            PageBorder.Background = FindResource("WindowBackground") as Brush ?? Brushes.Transparent;
            return;
        }
        if (IsGlassStyle())
        {
            // 玻璃：内容区半透明底（对齐 HTML .content rgba(15,17,21,.28)），不挂索引贴色带
            PageBorder.Background = TryFindResource("GlassContentBackground") as Brush
                ?? new SolidColorBrush(Color.FromArgb(0x47, 0x0F, 0x11, 0x15));
            return;
        }
        var winBg = (FindResource("WindowBackground") as SolidColorBrush)?.Color ?? Colors.White;
        var grad = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };
        grad.GradientStops.Add(new GradientStop(tab, 0.0));
        grad.GradientStops.Add(new GradientStop(tab, 0.10));
        grad.GradientStops.Add(new GradientStop(winBg, 0.55));
        grad.GradientStops.Add(new GradientStop(winBg, 1.0));
        PageBorder.Background = grad;
    }

    // ===== 侧边栏 =====

    private void BuildSidebar(MainTabKind kind)
    {
        SidebarItemsPanel.Children.Clear();
        _sidebarItems.Clear();

        // 清单 #16：玻璃侧栏恒为 42px 窄图标栏 —— 对齐 HTML .window.minimal 的
        // 「.sitem{height:34px;margin:4px 0;justify-content:center}」+「.ico{17px}」。
        // 若沿用 36px 行高与 14/12 横向内边距，42px 宽度下图标可用宽度只剩 16px，两侧会被裁掉。
        var glassBar = IsGlassStyle();
        SidebarItemsPanel.Margin = glassBar ? new Thickness(5, 8, 5, 8) : new Thickness(6, 8, 6, 8);

        if (!Sidebar.Has(kind))
        {
            SidebarRoot.Visibility = Visibility.Collapsed;
            return;
        }

        SidebarRoot.Visibility = Visibility.Visible;

        foreach (var it in Sidebar.For(kind))
        {
            var row = new Grid
            {
                Height = glassBar ? 34 : 36,
                Margin = glassBar ? new Thickness(0, 4, 0, 4) : new Thickness(0, 2, 0, 2),
                Cursor = Cursors.Hand,
                Tag = it.Id,
                Background = Brushes.Transparent
            };

            var indicator = new Rectangle
            {
                Width = SidebarState.IndicatorWidth,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                // HTML .sitem .ind{top:8px;bottom:8px}：窄栏下指示条两端留白，不顶满整行
                Margin = glassBar ? new Thickness(0, 8, 0, 8) : new Thickness(0),
                Fill = (Brush)Application.Current.Resources["SidebarIndicatorBrush"],
                Visibility = Visibility.Collapsed
            };

            var inner = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = glassBar ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
                Margin = glassBar ? new Thickness(0) : new Thickness(14, 0, 12, 0)
            };
            var icon = new PngIcon { Token = it.Icon, Size = glassBar ? 17 : 18 };
            var title = new TextBlock
            {
                Text = LocaleManager.T(it.Title),
                Foreground = (Brush)FindResource("SecondaryForeground"),
                FontSize = 13,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = (!glassBar && _sidebarState.Expanded) ? Visibility.Visible : Visibility.Collapsed
            };
            inner.Children.Add(icon);
            inner.Children.Add(title);

            row.Children.Add(indicator);
            row.Children.Add(inner);

            // 让侧边栏项可获取键盘焦点（默认 Grid 不可聚焦），以支持上下方向键切换副页；
            // 取消焦点视觉样式，避免聚焦时出现虚线框。
            row.Focusable = true;
            row.FocusVisualStyle = null;
            row.KeyDown += SidebarRow_KeyDown;
            row.MouseLeftButtonUp += (_, _) => SelectSidebarItem(it.Id);
            // 侧边栏项悬浮：仅背景高亮（对齐 HTML 的 .sitem:hover{background}，无缩放弹跳）
            row.MouseEnter += (_, _) => row.Background = (Brush)FindResource("ControlHoverBackground");
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

            SidebarItemsPanel.Children.Add(row);
            _sidebarItems[it.Id] = new SidebarParts(row, indicator, title, icon);
        }

        UpdateSidebarSelection();
        // 初次构建（含切换主标签）：让默认选中项在可滚动时居中
        RequestSidebarCenter();
    }

    private void UpdateSidebarSelection()
    {
        var sel = _sidebarState.SelectedId;
        foreach (var (id, p) in _sidebarItems)
        {
            var active = id == sel;
            // 选中指示条：固定 3px 宽，opacity 平滑渐显/渐隐（对齐 HTML 的 .sitem.active .indicator{opacity:1}）
            p.Indicator.Width = SidebarState.IndicatorWidth;
            p.Indicator.Visibility = Visibility.Visible;
            if (AnimationsEnabled)
            {
                p.Indicator.BeginAnimation(Rectangle.OpacityProperty,
                    new DoubleAnimation(active ? 1 : 0, TimeSpan.FromMilliseconds(SidebarState.TransitionMs)));
            }
            else
            {
                p.Indicator.Opacity = active ? 1 : 0;
            }
            // 选中项用 PrimaryForeground（亮/暗主题均为强对比），避免亮底上白字不可见
            var fg = active ? (Brush)FindResource("PrimaryForeground") : (Brush)FindResource("SecondaryForeground");
            p.Title.Foreground = fg;
            // 高亮底必须「有的清、没的清」两侧都写：
            // 之前只在 active 时赋背景、从不重置，导致用键盘上下键切换副页时，
            // 上一个选中项（以及鼠标划过留下的 hover 底）永远亮着 —— 多个项同时高亮。
            p.Row.Background = active
                ? (Brush)FindResource("ControlHoverBackground")
                : Brushes.Transparent;
        }
    }

    /// <summary>
    /// 清单 #15：安卓风格的「顶部横向子标签栏」（对齐 HTML .window.android .top-tabs）——
    /// 42px 高、左对齐横向排布、胶囊状子标签，替代被隐藏的侧边栏充当副页切换入口。
    /// 数据源与侧边栏同源（<see cref="Sidebar.For"/>），选中项用当前主标签的主题色。
    /// </summary>
    private void BuildTopTabs(MainTabKind kind)
    {
        TopTabsPanel.Children.Clear();
        _topTabs.Clear();

        if (!IsAndroidStyle() || !Sidebar.Has(kind))
        {
            TopTabsBar.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var it in Sidebar.For(kind))
        {
            var text = new TextBlock
            {
                Text = LocaleManager.T(it.Title),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            var tab = new Border
            {
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                Child = text
            };

            var id = it.Id;
            tab.MouseLeftButtonUp += (_, _) => SelectSidebarItem(id);
            tab.MouseEnter += (_, _) =>
            {
                if (id == _sidebarState.SelectedId) return;
                tab.Background = TryFindResource("ControlHoverBackground") as Brush ?? Brushes.Transparent;
            };
            tab.MouseLeave += (_, _) =>
            {
                if (id == _sidebarState.SelectedId) return;
                tab.Background = Brushes.Transparent;
            };

            TopTabsPanel.Children.Add(tab);
            _topTabs[id] = new TopTabParts(tab, text);
        }

        TopTabsBar.Visibility = Visibility.Visible;
        SyncTopTabsSelection(_sidebarState.SelectedId);
    }

    /// <summary>同步安卓顶部子标签的选中态（HTML .ttab.active：主题色文字 + 14% 底 + 40% 描边）。</summary>
    private void SyncTopTabsSelection(string? selId)
    {
        if (_topTabs.Count == 0) return;
        var accent = TabColor($"Tab{_currentKind}Brush");
        var dim = TryFindResource("SecondaryForeground") as Brush ?? new SolidColorBrush(Colors.Gray);
        foreach (var (id, p) in _topTabs)
        {
            var active = id == selId;
            p.Text.Foreground = active ? new SolidColorBrush(accent) : dim;
            p.Root.Background = active
                ? new SolidColorBrush(Color.FromArgb(0x24, accent.R, accent.G, accent.B))
                : Brushes.Transparent;
            p.Root.BorderBrush = active
                ? new SolidColorBrush(Color.FromArgb(0x66, accent.R, accent.G, accent.B))
                : Brushes.Transparent;
        }
    }

    private void SelectSidebarItem(string id)
    {
        _sidebarState.Select(id);
        UpdateSidebarSelection();
        SyncTopTabsSelection(id);
        RouteSidebar(id);
        // 选中项变更（点击 / 键盘 / 程序切换）→ 自动居中到侧边栏垂直中央
        RequestSidebarCenter();
        // 让选中项获取键盘焦点，便于后续用方向键继续切换
        if (_sidebarItems.TryGetValue(id, out var p)) p.Row.Focus();
    }

    /// <summary>侧边栏项方向键导航：上下键在副页列表中移动选中项（移动后由 SelectSidebarItem 自动居中）。</summary>
    private void SidebarRow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Grid row) return;
        var dir = e.Key switch
        {
            Key.Up => -1,
            Key.Down => 1,
            _ => 0
        };
        if (dir == 0) return;

        var id = row.Tag as string;
        var items = Sidebar.For(_sidebarState.Owner);
        var idx = -1;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Id == id) { idx = i; break; }
        }
        if (idx < 0) return;

        var ni = idx + dir;
        if (ni < 0 || ni >= items.Count) { e.Handled = true; return; }
        SelectSidebarItem(items[ni].Id);   // 内部已处理居中 + 聚焦新项
        e.Handled = true;
    }

    /// <summary>把全局侧边栏副标签的点击路由到对应主视图（规格 1.4：侧边栏点击切换内容区）。</summary>
    private void RouteSidebar(string id)
    {
        switch (_currentKind)
        {
            case MainTabKind.Download:
                (_pages[MainTabKind.Download] as DownloadPageView)?.ShowSubTab(id);
                break;
            case MainTabKind.Toolbox:
                (_pages[MainTabKind.Toolbox] as ToolboxView)?.ShowPanel(id);
                break;
            case MainTabKind.Settings:
                (_pages[MainTabKind.Settings] as SettingsView)?.ShowSidebarItem(id);
                break;
        }
    }

    // ===== 侧边栏自动居中滚动（工具箱等副页较多时可滚动）=====

    /// <summary>
    /// 把「侧边栏垂直偏移」暴露为可动画的附加属性：动画每一帧回调里驱动
    /// <see cref="ScrollViewer.ScrollToVerticalOffset"/>，实现平滑滚动。
    /// </summary>
    private static readonly DependencyProperty SidebarVerticalOffsetProperty =
        DependencyProperty.RegisterAttached(
            "SidebarVerticalOffset", typeof(double), typeof(MainWindow),
            new PropertyMetadata(0d, (d, e) =>
            {
                if (d is ScrollViewer sv) sv.ScrollToVerticalOffset((double)e.NewValue);
            }));

    /// <summary>请求把当前选中项居中（延迟到布局完成后再计算，确保视口/范围尺寸有效）。</summary>
    private void RequestSidebarCenter()
    {
        if (SidebarScroll is null) return;
        SidebarScroll.Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
            new Action(ScrollSelectedSidebarItemToCenter));
    }

    /// <summary>
    /// 将当前选中项滚动到侧边栏垂直中央。
    /// 规则：列表未超出一屏不滚动；超出时计算居中偏移并钳制到 [0, max]，使靠近顶部/底部的项自然停靠边界。
    /// </summary>
    private void ScrollSelectedSidebarItemToCenter()
    {
        var scroll = SidebarScroll;
        if (scroll is null) return;
        var selId = _sidebarState.SelectedId;
        if (selId is null || !_sidebarItems.TryGetValue(selId, out var parts))
            return;

        double viewport = scroll.ViewportHeight;
        double extent = scroll.ExtentHeight;
        if (extent <= viewport) return;   // 全部可见 → 不触发滚动

        var item = parts.Row;
        // 选中项相对内容顶部（面板坐标系）的偏移，与当前滚动位置无关
        var itemTop = item.TransformToVisual(SidebarItemsPanel).Transform(new Point(0, 0)).Y;
        var itemHeight = item.ActualHeight;
        var itemCenter = itemTop + itemHeight / 2;

        double target = itemCenter - viewport / 2;
        double maxOffset = extent - viewport;
        target = Math.Max(0, Math.Min(target, maxOffset));   // 钳制：顶部/底部自然停靠

        if (AnimationsEnabled)
        {
            var anim = new DoubleAnimation(scroll.VerticalOffset, target, TimeSpan.FromMilliseconds(220));
            anim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            scroll.BeginAnimation(SidebarVerticalOffsetProperty, anim);
        }
        else
        {
            scroll.ScrollToVerticalOffset(target);
        }
    }

    private void Sidebar_MouseEnter(object sender, MouseEventArgs e)
    {
        // 清单 #16：玻璃风格侧栏恒为 42px 窄图标栏，不随悬浮展开
        if (IsGlassStyle()) return;
        _collapseTimer?.Stop();
        if (_sidebarState.Expanded)
        {
            AnimateSidebar(_sidebarState.Width, _sidebarState.Expanded);
            return;
        }
        _expandTimer?.Stop();
        _expandTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SidebarState.HoverExpandDelayMs) };
        _expandTimer.Tick += (_, _) =>
        {
            _expandTimer?.Stop();
            _sidebarState.HoverEnter();
            AnimateSidebar(_sidebarState.Width, _sidebarState.Expanded);
        };
        _expandTimer.Start();
    }

    private void Sidebar_MouseLeave(object sender, MouseEventArgs e)
    {
        if (IsGlassStyle()) return;
        _expandTimer?.Stop();
        _collapseTimer?.Stop();
        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SidebarState.HoverCollapseDelayMs) };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer?.Stop();
            _sidebarState.HoverLeave();
            AnimateSidebar(_sidebarState.Width, _sidebarState.Expanded);
        };
        _collapseTimer.Start();
    }

    // bug2.txt #5：四色索引贴选中指示条统一奶白色（不再按贴色变化）
    private static readonly SolidColorBrush CreamUnderline = new(Color.FromRgb(0xF2, 0xE9, 0xD8));

    private void AnimateSidebar(double width, bool expanded)
    {
        // bug2.txt #1：关闭「动画效果」时跳过过渡动画，直接落到终态
        if (!AnimationsEnabled)
        {
            SidebarRoot.Width = width;
            foreach (var p in _sidebarItems.Values)
                p.Title.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        SidebarRoot.BeginAnimation(FrameworkElement.WidthProperty,
            new DoubleAnimation(width, TimeSpan.FromMilliseconds(SidebarState.TransitionMs)));
        foreach (var p in _sidebarItems.Values)
            p.Title.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
    }

    // ===== 导航 =====

    private void SelectTab(MainTabKind kind)
    {
        if (kind == _currentKind) return;
        NavigateTo(kind);
    }

    private void NavigateTo(MainTabKind kind)
    {
        if (!_pages.TryGetValue(kind, out var page) || page is null) return;

        PageHost.Content = page;
        _currentKind = kind;

        SetTabTheme(kind);
        ApplyTabLayout(kind);
        ApplyBottomNavSelection(kind);
        BuildTopTabs(kind);

        _sidebarState.SwitchOwner(kind);
        BuildSidebar(kind);
        // 清单 #16：玻璃风格侧栏恒为 42px 且不展开
        var glassNav = IsGlassStyle();
        AnimateSidebar(Sidebar.Has(kind) ? (glassNav ? 42 : _sidebarState.Width) : 0,
            glassNav ? false : _sidebarState.Expanded);

        // 进入各主视图时同步加载当前选中的副标签内容（规格 1.4 / 2.2）
        RouteSidebar(_sidebarState.SelectedId);

        // bug3.txt #3：版本大页（版本库 / 版本设置）绑定在游戏页下。
        // 离开游戏页时隐藏版本大页（仍可切大页），回到游戏页且曾打开时恢复（保留状态）。
        if (kind == MainTabKind.Game && _gameBigPage is not null)
            BigPageHost.Visibility = Visibility.Visible;
        else
            BigPageHost.Visibility = Visibility.Collapsed;

        if (AnimationsEnabled) PlayPageTransition();
    }

    // ===== 彩蛋（清单 #34）=====

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == KonamiSequence[_konamiIndex])
        {
            _konamiIndex++;
            if (_konamiIndex < KonamiSequence.Length) return;
            _konamiIndex = 0;
            OpenEasterEgg();
            return;
        }
        // 断链时允许以序列首键重新起头
        _konamiIndex = e.Key == KonamiSequence[0] ? 1 : 0;
    }

    private void OpenEasterEgg()
    {
        try
        {
            var win = new EasterEggGameWindow { Owner = this };
            win.Show();
        }
        catch { /* 彩蛋非关键 */ }
    }

    // ===== 界面风格（清单 #15：沉底导航）=====

    /// <summary>
    /// 按当前界面风格调整导航形态：安卓式 → 隐藏顶部四色索引贴、显示沉底导航；
    /// 其它风格 → 恢复顶部索引贴、隐藏沉底导航。
    /// </summary>
    private void ApplyUiStyle()
    {
        var kind = UiStyles.Parse(ThemeManager.UiStyle);
        var android = kind == UiStyleKind.Android;
        var dynamic = kind == UiStyleKind.Dynamic;
        var glass = kind == UiStyleKind.Glass;

        // 先复位「外壳」到 XAML 默认（标题栏/状态栏行高、品牌文案、搜索栏、侧栏、毛玻璃背板），
        // 再由下方各风格分支按需重设——保证任意风格互切后状态干净。
        RestoreDefaultChrome();

        // 顶部四色索引贴：安卓隐藏（改沉底导航），标准/灵动保留（灵动让贴浮在彩色卡片上）
        TabPanel.Visibility = android ? Visibility.Collapsed : Visibility.Visible;
        BottomNavBar.Visibility = android ? Visibility.Visible : Visibility.Collapsed;

        if (dynamic)
        {
            // 灵动：标题栏透明 + 侧栏透明 + 内容区变圆角彩色卡片
            TitleBar.Background = Brushes.Transparent;
            SidebarRoot.Visibility = Visibility.Visible;
            SidebarRoot.Width = 56;
            SidebarRoot.Background = Brushes.Transparent;
            SidebarRoot.BorderThickness = new Thickness(0);
            BottomNavBar.ClearValue(Border.HeightProperty);
            SetDynamicCard(true);
        }
        else if (android)
        {
            // 安卓：标题栏强调色实底 + 隐藏侧栏 + 沉底四色导航（64px）
            TitleBar.Background = (TryFindResource("AccentBrush") as SolidColorBrush) ?? Brushes.DodgerBlue;
            SidebarRoot.Visibility = Visibility.Collapsed;
            BottomNavBar.Height = 64;
            SetDynamicCard(false);
        }
        else if (glass)
        {
            // 玻璃：终端式标题栏 / 状态栏 + 窄半透明侧栏 + 系统级毛玻璃背板
            ApplyGlassChrome();
        }
        else
        {
            // 标准：外壳已由 RestoreDefaultChrome 复位（标题栏随主标签变色、侧栏随主题）
            BottomNavBar.ClearValue(Border.HeightProperty);
            SetDynamicCard(false);
        }

        // 清单 #16：风格切换后必须**重算**随风格变化的部件，否则会残留上一风格的外观 ——
        // 索引贴（玻璃的浏览器式圆角/白描边/等宽字 ↔ 其余的四色贴纸）、内容底色带、
        // 以及侧栏项（玻璃 17px 窄图标 ↔ 其余带文字）。此前只在切换主标签时才重算，
        // 导致「从毛玻璃切到其他风格」后索引贴仍是中性灰块、文字几乎不可见。
        if (_currentKind != (MainTabKind)(-1) && _tabs.Count > 0)
        {
            ApplyTabLayout(_currentKind);
            ApplyPageTint(_currentKind);
            BuildSidebar(_currentKind);
            BuildTopTabs(_currentKind);

            // 侧栏可见性与宽度按当前风格收口：安卓隐藏、玻璃恒 42px、其余沿用用户当前展开状态
            SidebarRoot.BeginAnimation(FrameworkElement.WidthProperty, null);
            if (android || !Sidebar.Has(_currentKind))
            {
                SidebarRoot.Visibility = Visibility.Collapsed;
            }
            else
            {
                SidebarRoot.Visibility = Visibility.Visible;
                SidebarRoot.Width = glass ? 42 : _sidebarState.Width;
            }
        }

        ApplyBottomNavSelection(_currentKind);
    }

    /// <summary>把标题栏 / 侧栏 / 状态栏的外壳复位到 XAML 默认，供风格切换时还原（含关闭毛玻璃背板）。</summary>
    private void RestoreDefaultChrome()
    {
        SidebarItemsPanel.Margin = new Thickness(6, 8, 6, 8);
        TitleBarRow.Height = _titleBarRowHeight;
        StatusBarRow.Height = _statusBarRowHeight;

        BrandText.Text = _brandTextDefault;
        BrandText.FontFamily = _brandFontDefault;
        BrandText.FontSize = _brandFontSizeDefault;
        SearchArea.Visibility = Visibility.Visible;
        SearchColumn.Width = new GridLength(272);
        TabPanel.HorizontalAlignment = HorizontalAlignment.Right;

        TitleBar.SetResourceReference(Panel.BackgroundProperty, "TitleBarBrush");

        SidebarRoot.Visibility = Visibility.Visible;
        SidebarRoot.Width = 56;
        SidebarRoot.SetResourceReference(Border.BackgroundProperty, "ControlBackground");
        SidebarRoot.SetResourceReference(Border.BorderBrushProperty, "SidebarEdgeBrush");
        SidebarRoot.BorderThickness = new Thickness(0, 0, 1, 0);

        StatusBarRoot.SetResourceReference(Control.BackgroundProperty, "ControlBackground");
        StatusBarRoot.FontFamily = _statusFontDefault;
        StatusBarRoot.FontSize = _statusFontSizeDefault;

        if (_backdropActive) DisableBackdrop();
        SetResourceReference(BackgroundProperty, "WindowBackground");
    }

    /// <summary>清单 #16：玻璃外壳——40px 终端标题栏（&gt; Chert、隐藏搜索）+ 42px 半透明侧栏 + 26px 终端状态栏。</summary>
    private void ApplyGlassChrome()
    {
        TitleBarRow.Height = new GridLength(40);
        StatusBarRow.Height = new GridLength(26);

        TitleBar.Background = TryFindResource("GlassChromeBackground") as Brush ?? Brushes.Transparent;
        BrandText.Text = "> Chert";
        BrandText.FontFamily = MonoFont;
        BrandText.FontSize = 14;
        SearchArea.Visibility = Visibility.Collapsed;
        // 搜索列是固定宽度，隐藏搜索栏不会让它塌陷 —— 必须显式收为 0，
        // 浏览器式标签才能紧贴品牌文字从左侧排开（HTML：tab 容器 left:96px、justify-content:flex-start）
        SearchColumn.Width = new GridLength(0);
        TabPanel.HorizontalAlignment = HorizontalAlignment.Left;

        SidebarRoot.Visibility = Visibility.Visible;
        SidebarRoot.Width = 42;
        SidebarRoot.Background = TryFindResource("GlassSidebarBackground") as Brush ?? Brushes.Transparent;
        SidebarRoot.BorderThickness = new Thickness(0, 0, 1, 0);
        SidebarRoot.BorderBrush = TryFindResource("GlassSidebarBorder") as Brush ?? Brushes.Transparent;
        // 玻璃侧栏恒为窄图标栏：收起所有文字标签
        foreach (var p in _sidebarItems.Values) p.Title.Visibility = Visibility.Collapsed;

        StatusBarRoot.Background = TryFindResource("GlassChromeBackground") as Brush ?? Brushes.Transparent;
        StatusBarRoot.FontFamily = MonoFont;
        StatusBarRoot.FontSize = 11;

        BottomNavBar.ClearValue(Border.HeightProperty);
        SetDynamicCard(false);
        ApplyGlassSurface(TryEnableBackdrop());
    }

    /// <summary>
    /// 玻璃窗体底色：背板可用时**完全透明**（让 DWM 的虚化桌面从窗口后面透出来），
    /// 不可用时换成同色不透明底，避免半透明层叠在无背板的黑底上发灰。
    /// <para>
    /// 关键：只调 DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE) 是**不够的**。
    /// DWM 的背板只绘制在「DWM 自己渲染的窗口区域」里，而 WPF 窗口的客户区全部由 WPF 自绘，
    /// 背板会被整个盖住 —— 表现就是「系统返回成功，但一点模糊都看不到」。
    /// 必须同时让出 WindowChrome 的 GlassFrameThickness=-1（见 MainWindow.xaml），
    /// 并让窗口底为 Transparent，背板才可见。
    /// </para>
    /// </summary>
    private void ApplyGlassSurface(bool backdropActive)
    {
        if (backdropActive)
        {
            Background = Brushes.Transparent;
            return;
        }
        if (TryFindResource("WindowBackground") is SolidColorBrush bg)
        {
            // 无背板能力（Win10 早期 / 远程桌面 / 被系统设置关闭）：同色不透明底兜底
            Background = new SolidColorBrush(Color.FromArgb(0xFF, bg.Color.R, bg.Color.G, bg.Color.B));
            return;
        }
        SetResourceReference(BackgroundProperty, "WindowBackground");
    }

    /// <summary>清单 #16：玻璃标签改「浏览器式」外观——顶部圆角、白 8% 描边、中性半透明底、等宽字、无下划线。</summary>
    private void ApplyGlassTabChrome(TabParts p, bool isSel)
    {
        var baseBg = (TryFindResource("GlassTabBackground") as SolidColorBrush)?.Color ?? Colors.Transparent;
        var activeBg = (TryFindResource("GlassTabActiveBackground") as SolidColorBrush)?.Color ?? baseBg;
        var hoverBg = (TryFindResource("GlassTabHoverBackground") as SolidColorBrush)?.Color ?? baseBg;

        p.Brush.Color = isSel ? activeBg : baseBg;
        p.BaseColor = p.Brush.Color;
        p.HoverColor = hoverBg;
        p.Bg.Background = p.Brush;
        p.Bg.CornerRadius = new CornerRadius(8, 8, 0, 0);
        p.Bg.BorderThickness = new Thickness(1, 1, 1, 0);
        // HTML：常态 color:var(--fg2)、active/hover color:var(--fg)；active 描边提到白 12%
        p.Bg.BorderBrush = isSel
            ? new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF))
            : TryFindResource("GlassTabBorder") as Brush;
        p.Title.FontFamily = MonoFont;
        p.Title.FontSize = 12;
        p.Title.Foreground = (Brush)FindResource(isSel ? "PrimaryForeground" : "SecondaryForeground");
        p.Underline.Visibility = Visibility.Collapsed;
    }

    /// <summary>恢复四色索引贴默认外观（玻璃 → 其他风格切换时）。</summary>
    private static void ApplyDefaultTabChrome(TabParts p, int index, int last)
    {
        p.Root.MinWidth = 0;
        p.Bg.CornerRadius = TabCornerRadius(index, last);
        p.Bg.BorderThickness = new Thickness(0);
        p.Bg.BorderBrush = null;
        p.Title.ClearValue(TextBlock.FontFamilyProperty);
        p.Title.FontSize = 13;
        p.Title.Foreground = Brushes.White;
    }

    /// <summary>清单 #17：灵动风格判定（透明标题栏 + 整页圆角彩色卡片 + 卡片横向滑动）。</summary>
    private static bool IsDynamicStyle() =>
        UiStyles.Parse(ThemeManager.UiStyle) == UiStyleKind.Dynamic;

    /// <summary>清单 #15：安卓风格判定（沉底四色导航 + 隐藏侧栏 + 内容横向滑动）。</summary>
    private static bool IsAndroidStyle() =>
        UiStyles.Parse(ThemeManager.UiStyle) == UiStyleKind.Android;

    /// <summary>清单 #16：玻璃风格判定（终端标题栏 + 窄半透明侧栏 + 毛玻璃窗体）。</summary>
    private static bool IsGlassStyle() =>
        UiStyles.Parse(ThemeManager.UiStyle) == UiStyleKind.Glass;

    /// <summary>清单 #17：灵动风格下把内容宿主（PageBorder）变为整页圆角彩色卡片；其余风格恢复默认。</summary>
    private void SetDynamicCard(bool on)
    {
        if (PageBorder is null) return;
        if (on)
        {
            PageBorder.Margin = new Thickness(8);
            PageBorder.CornerRadius = new CornerRadius(18);
            PageBorder.BorderThickness = new Thickness(0);
            PageBorder.SizeChanged -= UpdateDynamicCardClip;
            PageBorder.SizeChanged += UpdateDynamicCardClip;
            UpdateDynamicCardClip(null, null);
        }
        else
        {
            PageBorder.SizeChanged -= UpdateDynamicCardClip;
            PageBorder.Clip = null;
            PageBorder.Margin = new Thickness(0);
            PageBorder.CornerRadius = new CornerRadius(0);
            if (_currentKind != (MainTabKind)(-1)) ApplyPageTint(_currentKind);
        }
    }

    /// <summary>清单 #17：把内容宿主裁剪为圆角矩形，使彩色卡片四角圆滑（Border 不会自动裁剪子内容）。</summary>
    private void UpdateDynamicCardClip(object? sender, SizeChangedEventArgs e)
    {
        if (PageBorder is null) return;
        // 布局前 ActualWidth/Height 为 0，暂不裁剪，避免卡片被裁成零尺寸而整片空白；
        // 等待首个 SizeChanged（真实尺寸）再施加圆角裁剪。
        if (PageBorder.ActualWidth <= 0 || PageBorder.ActualHeight <= 0) return;
        var r = PageBorder.CornerRadius.TopLeft;
        PageBorder.Clip = new RectangleGeometry(new Rect(0, 0, PageBorder.ActualWidth, PageBorder.ActualHeight), r, r);
    }

    /// <summary>刷新沉底导航项的选中态（图标 / 文字取强调色，选中项加胶囊高亮底）。</summary>
    private void ApplyBottomNavSelection(MainTabKind selected)
    {
        foreach (var kv in _bottomNav)
        {
            var active = kv.Key == selected;
            var dim = TryFindResource("SecondaryForeground") as SolidColorBrush ?? new SolidColorBrush(Colors.Gray);
            // 安卓四色导航：选中项用该标签主题色（四色之一），其余风格沿用强调色
            if (IsAndroidStyle())
            {
                var tc = TabColor($"Tab{kv.Key}Brush");
                kv.Value.Title.Foreground = active ? new SolidColorBrush(tc) : dim;
                kv.Value.Pill.Background = active
                    ? new SolidColorBrush(Color.FromArgb(0x22, tc.R, tc.G, tc.B))
                    : Brushes.Transparent;
            }
            else
            {
                var accent = TryFindResource("AccentBrush") as SolidColorBrush ?? new SolidColorBrush(Colors.DodgerBlue);
                kv.Value.Title.Foreground = active ? accent : dim;
                kv.Value.Pill.Background = active
                    ? new SolidColorBrush(Color.FromArgb(0x22, accent.Color.R, accent.Color.G, accent.Color.B))
                    : Brushes.Transparent;
            }
            kv.Value.Icon.Opacity = active ? 1.0 : 0.65;
        }
    }

    // ===== 版本库大页（bug #10，bug3.txt #3 绑定游戏页）=====

    private void ShowBigPage(FrameworkElement page)
    {
        _gameBigPage = page;
        BigPageHost.Children.Clear();
        BigPageHost.Children.Add(page);
        BigPageHost.Visibility = Visibility.Visible;
    }

    private void CloseBigPage()
    {
        _gameBigPage = null;
        BigPageHost.Visibility = Visibility.Collapsed;
        BigPageHost.Children.Clear();
    }

    private void PlayPageTransition()
    {
        // 灵动：卡片横向滑入（对齐 HTML 的 .page-card 左右平移），其余风格维持垂直滑入
        if (IsDynamicStyle())
        {
            var dur = TimeSpan.FromMilliseconds(120);
            PageBorder.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, dur));
            PageTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(40, 0, dur));
            return;
        }
        // 玻璃：内容横向滑动（对齐 HTML .page.leave/enter 的左右平移，180ms）
        if (IsGlassStyle())
        {
            var gdur = TimeSpan.FromMilliseconds(180);
            PageTransform.Y = 0;
            PageBorder.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, gdur));
            PageTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(28, 0, gdur));
            return;
        }
        // 安卓：内容横向滑动（对齐 HTML .page.leave/enter 的左右平移）
        if (IsAndroidStyle())
        {
            var dur = TimeSpan.FromMilliseconds(180);
            PageTransform.Y = 0;
            PageBorder.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, dur));
            PageTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(30, 0, dur));
            return;
        }
        var duration = TimeSpan.FromMilliseconds(200);
        var fade = new DoubleAnimation(0, 1, duration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var slide = new DoubleAnimation(18, 0, duration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        PageBorder.BeginAnimation(UIElement.OpacityProperty, fade);
        PageTransform.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    /// <summary>四色索引贴入场动画：首次加载时错峰滑入 + 淡入（每贴延迟 90ms，缓出），还原自 HTML 参考的贴纸揭示效果。</summary>
    private void PlayTabEntrance()
    {
        if (!AnimationsEnabled) return;
        var i = 0;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        foreach (var p in _tabs.Values)
        {
            p.Root.Opacity = 0;
            p.Lift.Y = 14;
            var delay = TimeSpan.FromMilliseconds(90 * i);
            var dur = TimeSpan.FromMilliseconds(380);
            p.Root.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, dur) { BeginTime = delay, EasingFunction = ease });
            p.Lift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, dur) { BeginTime = delay, EasingFunction = ease });
            i++;
        }
    }

    // 注：下划线呼吸动画已移除 —— 对齐 HTML 完美版的克制平稳风
    // （选中下划线 opacity 渐显后常驻，不循环呼吸）。

    // ===== 窗口控制 =====

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        // bug #12：标题栏此前无条件 DragMove()，点击全局搜索框时事件冒泡至此，
        // 拖拽立刻抢走鼠标捕获，搜索框永远无法获得焦点 —— 表现为"搜索栏失效"。
        // 因此命中可交互控件（输入框 / 按钮 / 下拉框等）时不触发窗口拖动。
        if (IsInteractiveElement(e.OriginalSource as DependencyObject))
            return;

        try { DragMove(); }
        catch (InvalidOperationException) { /* 拖动期间窗口状态变化，忽略 */ }
    }

    /// <summary>判断命中元素是否位于可交互控件（TextBox / 按钮 / ComboBox 等）内部。</summary>
    private static bool IsInteractiveElement(DependencyObject? src)
    {
        while (src is not null)
        {
            if (src is System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.Primitives.ButtonBase
                or System.Windows.Controls.ComboBox
                or System.Windows.Controls.PasswordBox
                or System.Windows.Controls.Primitives.Thumb)
                return true;

            src = src is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(src)
                : LogicalTreeHelper.GetParent(src);
        }
        return false;
    }

    /// <summary>展开 / 收起标题栏下载队列弹窗（bug #14）。</summary>
    private void DownloadPopupBtn_Click(object sender, RoutedEventArgs e)
    {
        // 页面可能在窗口构造后才首次实例化，这里兜底再绑一次
        if (DownloadQueuePopup.DataContext is null && DownloadPageViewModel.Current is { } vm)
        {
            DownloadPopupBtn.DataContext = vm;
            DownloadQueuePopup.DataContext = vm;
        }
        DownloadQueuePopup.IsOpen = !DownloadQueuePopup.IsOpen;
    }

    private void BtnMin_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>设置主窗口背景图片（bug #20：此前仅持久化路径、从未真正应用）。path 为空或文件不存在时隐藏。</summary>
    public void SetBackgroundImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            BackgroundImage.Source = null;
            BackgroundImage.Visibility = Visibility.Collapsed;
            return;
        }
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            BackgroundImage.Source = bmp;
            BackgroundImage.Visibility = Visibility.Visible;
        }
        catch
        {
            BackgroundImage.Source = null;
            BackgroundImage.Visibility = Visibility.Collapsed;
        }
    }

    // ===== 工具 =====

    private static SolidColorBrush Brush(string key) =>
        (SolidColorBrush)Application.Current.FindResource(key);

    private static void AnimateWidth(FrameworkElement el, double to) =>
        el.BeginAnimation(FrameworkElement.WidthProperty,
            new DoubleAnimation(to, TimeSpan.FromMilliseconds(MainTabs.TransitionMs)));

    private static void AnimateMargin(FrameworkElement el, Thickness to) =>
        el.BeginAnimation(FrameworkElement.MarginProperty,
            new ThicknessAnimation(to, TimeSpan.FromMilliseconds(MainTabs.TransitionMs)));

    /// <summary>全局搜索：回车后若命中设置关键词则跳转到对应设置子项，否则跳下载页并预填搜索词（bug #23）。</summary>
    private void GlobalSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var text = GlobalSearchBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        // 命中设置子项关键词 → 跳转设置对应分类
        var settingsTarget = MatchSettingsSubItem(text);
        if (settingsTarget is not null && _pages[MainTabKind.Settings] is SettingsView sv)
        {
            NavigateTo(MainTabKind.Settings);
            sv.ShowSidebarItem(settingsTarget);
            return;
        }

        // bug #17：命中工具箱面板（日志管理/音乐播放器/存档管理…）→ 跳转工具箱并预填搜索词
        if (_pages[MainTabKind.Toolbox] is ToolboxView tbv && tbv.MatchPanelId(text) is { } panelId)
        {
            NavigateTo(MainTabKind.Toolbox);
            tbv.ShowPanel(panelId);
            tbv.SetSearchKeyword(text);
            return;
        }

        // 默认：跳转到下载页并设置搜索词
        NavigateTo(MainTabKind.Download);
        if (_pages[MainTabKind.Download] is DownloadPageView dpv)
        {
            dpv.SetSearchKeyword(text);
        }
    }

    /// <summary>将搜索词匹配到设置子项 id（general/launch/download/recommend/account/ai/appearance/about）。无匹配返回 null。</summary>
    private static string? MatchSettingsSubItem(string raw)
    {
        var t = raw.ToLowerInvariant();
        // 先匹配最具体的词，避免被通用词误命中
        if (t.Contains("主题") || t.Contains("外观") || t.Contains("背景") || t.Contains("字体") || t.Contains("颜色") ||
            t.Contains("theme") || t.Contains("appearance") || t.Contains("background") || t.Contains("font"))
            return "appearance";
        if (t.Contains("账号") || t.Contains("账户") || t.Contains("登录") || t.Contains("微软") ||
            t.Contains("account") || t.Contains("login") || t.Contains("microsoft"))
            return "account";
        if (t.Contains("启动") || t.Contains("内存") || t.Contains("java") || t.Contains("游戏路径") || t.Contains("路径") ||
            t.Contains("launch") || t.Contains("memory") || t.Contains("ram"))
            return "launch";
        if (t.Contains("下载") || t.Contains("源") || t.Contains("镜像") || t.Contains("并发") ||
            t.Contains("download") || t.Contains("mirror") || t.Contains("source"))
            return "download";
        if (t.Contains("推荐") || t.Contains("recommend"))
            return "recommend";
        if (t.Contains("ai") || t.Contains("助手") || t.Contains("ollama") || t.Contains("assistant"))
            return "ai";
        if (t.Contains("语言") || t.Contains("自启") || t.Contains("托盘") || t.Contains("通用") ||
            t.Contains("language") || t.Contains("general") || t.Contains("autostart"))
            return "general";
        if (t.Contains("关于") || t.Contains("更新") || t.Contains("版本") ||
            t.Contains("about") || t.Contains("update") || t.Contains("version"))
            return "about";
        return null;
    }

    private sealed class TabParts
    {
        public Grid Root;
        public Border Bg;
        public TextBlock Title;
        public Rectangle Underline;
        // 每贴独立克隆画刷：避免悬浮动画连累合并字典里的共享/冻结画刷
        public SolidColorBrush Brush = new SolidColorBrush(Colors.Transparent);
        public Color BaseColor;   // 静止色（选中=提亮色，未选中=实色）
        public Color HoverColor;  // 悬浮提亮色
        public ScaleTransform Scale;        // 悬浮弹跳 / 入场缩放
        public TranslateTransform Lift;     // 悬浮上浮 / 入场滑入
        public TabParts(Grid root, Border bg, TextBlock title, Rectangle underline, ScaleTransform scale, TranslateTransform lift)
        {
            Root = root; Bg = bg; Title = title; Underline = underline;
            Scale = scale; Lift = lift;
        }
    }

    private sealed record SidebarParts(Grid Row, Rectangle Indicator, TextBlock Title, FrameworkElement Icon);

    /// <summary>清单 #15：沉底导航项的可视部件。</summary>
    private sealed record BottomNavParts(Grid Root, Border Pill, TextBlock Title, FrameworkElement Icon);

    /// <summary>清单 #15：安卓顶部横向子标签的可视部件。</summary>
    private sealed record TopTabParts(Border Root, TextBlock Text);
}
