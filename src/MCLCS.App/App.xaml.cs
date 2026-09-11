using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using MCLCS.Core.Localization;
using MCLCS.Core.Launcher;
using MCLCS.Core.Profiles;
using MCLCS.Core.Theme;
using MCLCS.Core.Utils;

namespace MCLCS.App;

public partial class App : Application
{
    /// <summary>防止崩溃处理过程中重入导致级联弹窗。</summary>
    private static bool _crashHandling;

    /// <summary>
    /// 静态构造函数：注册进程级未处理异常钩子。
    /// 必须在任何实例构造之前注册，才能捕获 Application 基类 LoadBaml（App.xaml 解析）
    /// 阶段的启动期崩溃——这类异常早于 <see cref="App"/> 实例构造函数与 OnStartup，
    /// 早先的崩溃处理器（注册在实例构造函数里）完全抓不到，表现为"点开 exe 没反应、零日志"。
    /// </summary>
    static App()
    {
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public App()
    {
        // UI 线程未处理异常：写日志并弹窗，标记 Handled=true 让界面继续（不再静默崩溃退出）。
        DispatcherUnhandledException += (_, e) =>
        {
            WriteCrashLog("DispatcherUnhandledException", e.Exception);
            ShowFatalBox("启动器界面发生未处理异常，已写入 chert_crash.log。", e.Exception);
            e.Handled = true;
        };
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        WriteCrashLog("AppDomain.UnhandledException", ex);
        if (e.IsTerminating)
            ShowFatalBox("启动器发生未处理异常，已写入 chert_crash.log，即将退出。", ex);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // 后台任务异常：仅记录，标记为已观察，不终止进程。
        WriteCrashLog("TaskScheduler.UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    /// <summary>将崩溃信息追加写入 exe 同目录的 chert_crash.log（single-file 下用 Environment.ProcessPath 取真实路径）。</summary>
    private static void WriteCrashLog(string context, Exception? ex)
    {
        if (_crashHandling) return;
        _crashHandling = true;
        try
        {
            // 崩溃日志路径跟随 exe 实际文件名：GUI 启动器输出“Chert Launcher.exe”（AssemblyName 仍保留 MCLCS.App，
            // 仅用 RenameLauncherToChert 目标复制输出文件名），CLI 工具输出 chert.exe。优先用 Environment.ProcessPath 取真实路径，
            // 兜底取当前进程主模块路径，确保无论改名与否都能定位到真正的 exe 同目录。
            var exePath = Environment.ProcessPath
                          ?? (System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName)
                          ?? Path.Combine(AppContext.BaseDirectory, "Chert Launcher.exe");
            var dir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
            var logPath = Path.Combine(dir, "chert_crash.log");
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 燧石启动器崩溃（{context}）");
            sb.AppendLine($"版本：{GameConstants.LauncherVersion}");
            sb.AppendLine(ex?.ToString() ?? "(无异常对象)");
            sb.AppendLine(new string('-', 60));
            File.AppendAllText(logPath, sb.ToString());
        }
        catch
        {
            // 最后兜底：日志写入失败也不应再抛异常
        }
        finally
        {
            _crashHandling = false;
        }
    }

    /// <summary>尽可能用原生 MessageBox 展示致命错误（WPF/Win32 存活时可用）。</summary>
    private static void ShowFatalBox(string prefix, Exception? ex)
    {
        try
        {
            var detail = ex is null ? "" : $"\n\n{ex.GetType().Name}: {ex.Message}";
            MessageBox.Show(prefix + detail, $"{GameConstants.LauncherDisplayName} 崩溃", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // 极端情况下 UI 尚未就绪，忽略
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 必须最先执行：读取用户自定义的游戏目录，之后所有 GameConstants.DefaultGameRoot 才是正确值（bug #26）
        GameConstants.LoadGameRootOverride();
        MCLCS.App.Services.LauncherService.Reinitialize(GameConstants.DefaultGameRoot);

        // 载入上次保存的个人资料（语言 / 外观）
        LauncherProfile profile;
        try
        {
            profile = ProfileStore.Load(GameConstants.DefaultGameRoot);
            var lang = LocaleManager.NormalizeLocaleCode(profile.Language);
            LocaleManager.CurrentLocale = lang;
        }
        catch
        {
            // 首次启动 / profile 损坏 → 使用默认配置
            profile = new LauncherProfile();
        }

        // bug #19：首次安装（尚未配置 Java）时自动探测本机 Java 并持久化，避免每次都要手动「自动检测」。
        // 探测失败不影响启动，仅静默跳过。
        if (string.IsNullOrWhiteSpace(profile.JavaPath))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var best = await JavaDetector.FindBestAsync(GameConstants.MinimumJavaMajorVersion);
                    if (best is not null)
                    {
                        profile.JavaPath = best.JavaExe;
                        // ProfileStore.Save 依据 profile.GameRoot 落盘，首次启动时需兜底填充。
                        if (string.IsNullOrWhiteSpace(profile.GameRoot))
                            profile.GameRoot = GameConstants.DefaultGameRoot;
                        ProfileStore.Save(profile);
                    }
                }
                catch
                {
                    // 探测失败不影响启动
                }
            });
        }

        // 订阅主题变更事件
        ThemeManager.OnThemeChanged += ApplyTheme;

        // bug #28：HUD 叠加层应在所有启动路径（首页/版本列表/游戏详情/崩溃恢复）启动游戏后触发。
        // 统一订阅 Core 的游戏进程启动事件（GameLauncher.GameProcessStarted），覆盖全部入口；
        // TryShow 内部已切回 UI 线程，且会读取 profile.Hud.Enabled 决定是否显示。
        MCLCS.Core.Launcher.GameLauncher.GameProcessStarted += (proc, maxMb) =>
        {
            try { MCLCS.App.Views.HudOverlayWindow.TryShow(proc, maxMb); }
            catch { /* HUD 非关键，失败不影响游戏运行 */ }
        };

        // 启动即加载已保存的主题偏好并应用（修复：默认亮色启动 + 外观未持久化恢复）
        ThemeManager.LoadPreference(GameConstants.DefaultGameRoot);
        ApplyTheme(ThemeManager.Current);

        // 应用外观偏好：主题色 + 字体缩放，确保重启后恢复（bug #5 外观未持久化 / #10 字体缩放 / #11 主题色）
        ApplyAccentColor(profile.ThemeColor);
        ApplyFontScale(profile.FontScale);

        // 应用背景图片（bug #20：路径此前已持久化，但从未真正渲染到窗口）
        ApplyBackgroundImage(profile.BackgroundImagePath);
    }

    private void ApplyTheme(ThemeType theme)
    {
        var name = theme == ThemeType.Light ? "LightTheme.xaml" : "DarkTheme.xaml";
        var asm = typeof(App).Assembly.GetName().Name;
        var uri = new Uri($"pack://application:,,,/{asm};component/Themes/{name}", UriKind.Absolute);
        var newDict = new ResourceDictionary { Source = uri };

        // 仅替换主题字典，保留 Palette.xaml（四色索引贴颜色）/Controls.xaml（全局样式）资源，
        // 否则切换主题后这些资源键丢失会导致全局样式与索引贴颜色失效（bug #11）。
        var md = Resources.MergedDictionaries;
        for (int i = md.Count - 1; i >= 0; i--)
        {
            var src = md[i].Source?.ToString() ?? "";
            if (src.Contains("DarkTheme.xaml") || src.Contains("LightTheme.xaml"))
                md.RemoveAt(i);
        }
        md.Add(newDict);

        // 捕获主题真实边框色，供「显示控件边框」开关在开/关之间切换（控件边框开关）。
        // 控件均通过 DynamicResource 引用 ControlBorder/InputBorder，改顶层键即实时显隐。
        if (newDict.Contains("ControlBorder") && newDict["ControlBorder"] is SolidColorBrush cb)
            _realControlBorderColor = cb.Color;
        if (newDict.Contains("InputBorder") && newDict["InputBorder"] is SolidColorBrush ib)
            _realInputBorderColor = ib.Color;
        ApplyControlBorders(_controlBordersEnabled);
    }

    // ===== 控件边框开关 =====
    // 在 Application.Resources 顶层覆盖 ControlBorder/InputBorder（与 ApplyAccentColor 同一机制，
    // bug #11 已验证 DynamicResource 会实时跟随）。开启=主题真实边框色；关闭=透明（视觉隐藏边框，
    // 仍保留 1px 布局空间，不影响排版）。
    private static Color _realControlBorderColor = Color.FromRgb(0x2A, 0x2F, 0x3A);
    private static Color _realInputBorderColor = Color.FromRgb(0x2A, 0x2F, 0x3A);
    private static bool _controlBordersEnabled = true;

    public static void ApplyControlBorders(bool enabled)
    {
        _controlBordersEnabled = enabled;
        var res = Application.Current.Resources;
        res["ControlBorder"] = new SolidColorBrush(enabled ? _realControlBorderColor : Colors.Transparent);
        res["InputBorder"] = new SolidColorBrush(enabled ? _realInputBorderColor : Colors.Transparent);
    }

    // ===== 外观即时应用（主题色 / 字体缩放） =====

    /// <summary>将用户自定义主题色应用到全局 Accent 系列资源（bug #11：侧边栏/按键/滑动开关主题色失效）。</summary>
    public static void ApplyAccentColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return;
        if (!hex.StartsWith("#", StringComparison.Ordinal)) hex = "#" + hex;
        if (ColorConverter.ConvertFromString(hex) is not Color color) return;

        var res = Application.Current.Resources;
        res["AccentBrush"] = new SolidColorBrush(color);
        res["AccentHoverBrush"] = new SolidColorBrush(Darken(color, 0.18));
        res["AccentForeground"] = new SolidColorBrush(Colors.White);
        res["ProgressForeground"] = new SolidColorBrush(color);
        res["ButtonBackground"] = new SolidColorBrush(color);
        res["ButtonHoverBackground"] = new SolidColorBrush(Darken(color, 0.18));
    }

    /// <summary>将字体缩放系数应用到全局字号（bug #10：字体缩放失效）。</summary>
    public static void ApplyFontScale(double scale)
    {
        if (scale <= 0) scale = 1.0;
        var baseSize = 13.0 * scale;
        Application.Current.Resources["BaseFontSize"] = baseSize;
    }

    /// <summary>应用外观设置中的背景图片到主窗口（bug #20：此前仅持久化路径、从未真正应用）。</summary>
    public static void ApplyBackgroundImage(string? path)
    {
        try
        {
            if (Application.Current.MainWindow is MainWindow mw)
                mw.SetBackgroundImage(string.IsNullOrWhiteSpace(path) ? null : path);
        }
        catch
        {
            // 窗口尚未就绪等异常静默忽略
        }
    }

    private static Color Darken(Color c, double amount)
    {
        var f = 1.0 - amount;
        return Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
    }
}
