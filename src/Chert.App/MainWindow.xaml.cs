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
    // 侧边栏可视部件
    private readonly Dictionary<string, SidebarParts> _sidebarItems = new();
    private readonly SidebarState _sidebarState = new();

    private DispatcherTimer? _expandTimer;
    private DispatcherTimer? _collapseTimer;
    private TrayIconService? _tray;

    public MainWindow()
    {
        InitializeComponent();

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

        _sidebarState.SwitchOwner(MainTabKind.Game);
        BuildSidebar(MainTabKind.Game);

        // 四色索引贴入场动画在窗口 Loaded 后播一次（首次揭示效果，错峰滑入 + 淡入）
        Loaded += (_, _) => PlayTabEntrance();
        // bug #12：Win11 圆角
        Loaded += (_, _) => EnableWin11Corners();
        // bug #86：最大化按钮 + 限制到工作区（不覆盖任务栏）
        SourceInitialized += (_, _) => AttachMaximizeHook();
        Loaded += (_, _) => RefreshMaximizeIcon();
        // 窗口尺寸变化时（侧边栏可视高度改变）→ 重新把当前选中项居中
        SizeChanged += (_, _) => RequestSidebarCenter();
        StateChanged += (_, _) => RefreshMaximizeIcon();
        // bug #10：窗口就绪后尝试断点续播（MediaElement 此时已可播放）
        Loaded += (_, _) => MusicPlayerViewModel.Instance.RestoreLastState();

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
        }
    }

    /// <summary>按当前选中态刷新所有索引贴的宽度 / 配色 / 细线 / 重叠。</summary>
    private void ApplyTabLayout(MainTabKind selected)
    {
        var list = MainTabs.All;
        for (var i = 0; i < list.Count; i++)
        {
            var def = list[i];
            var p = _tabs[def.Kind];
            var isSel = def.Kind == selected;
            var expanded = isSel || def.AlwaysExpanded;
            var w = expanded ? MainTabs.ExpandedWidth : MainTabs.CollapsedWidth;

            AnimateWidth(p.Root, w);

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

            // 重叠：左侧邻居展开则 10px，否则 20px
            var left = i == 0 ? 0 :
                (NeighborExpanded(i, selected) ? -MainTabs.ExpandedOverlap : -MainTabs.CollapsedOverlap);
            AnimateMargin(p.Root, new Thickness(left, 0, 0, 0));

            // 选中贴抬到最上层（ZIndex=20），避免被左侧未选中贴的圆角/色块遮住展开后的文字
            Panel.SetZIndex(p.Root, isSel ? 20 : def.ZIndex);
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

        // 悬浮展开（仅对未展开的贴生效）：宽度过渡 width 0.25s ease + 文字淡入淡出
        if (!expandedNow)
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
                Height = 36,
                Margin = new Thickness(0, 2, 0, 2),
                Cursor = Cursors.Hand,
                Tag = it.Id,
                Background = Brushes.Transparent
            };

            var indicator = new Rectangle
            {
                Width = SidebarState.IndicatorWidth,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                Fill = (Brush)Application.Current.Resources["SidebarIndicatorBrush"],
                Visibility = Visibility.Collapsed
            };

            var inner = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 12, 0)
            };
            var icon = new PngIcon { Token = it.Icon, Size = 18 };
            var title = new TextBlock
            {
                Text = LocaleManager.T(it.Title),
                Foreground = (Brush)FindResource("SecondaryForeground"),
                FontSize = 13,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = _sidebarState.Expanded ? Visibility.Visible : Visibility.Collapsed
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
            if (active) p.Row.Background = (Brush)FindResource("ControlHoverBackground");
        }
    }

    private void SelectSidebarItem(string id)
    {
        _sidebarState.Select(id);
        UpdateSidebarSelection();
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

        _sidebarState.SwitchOwner(kind);
        BuildSidebar(kind);
        AnimateSidebar(Sidebar.Has(kind) ? _sidebarState.Width : 0, _sidebarState.Expanded);

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
}
