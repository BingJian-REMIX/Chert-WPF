using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Chert.Core.Ai;
using Chert.Core.Auth;
using Chert.Core.Download;
using Chert.Core.Hud;
using Chert.Core.Input;
using Chert.Core.Launcher;
using Chert.Core.Localization;
using Chert.Core.Music;
using Chert.Core.Mvvm;
using Chert.Core.Profiles;
using Chert.Core.Recommend;
using Chert.Core.Theme;
using Chert.Core.UI;
using Chert.Core.Update;
using Chert.Core.Utils;
using Chert.App.Services;
using Chert.App.Themes;

namespace Chert.App.ViewModels;

/// <summary>Java 下拉框选项：<see cref="Key"/> 为 java(.exe) 完整路径，<see cref="Display"/> 为下拉展示文本。</summary>
public sealed class JavaChoice
{
    public string Key { get; init; } = "";
    public string Display { get; init; } = "";
    public override string ToString() => Display;
}

/// <summary>玩法偏好勾选项。</summary>
public class CategoryPref : ObservableObject
{
    public GameplayCategory Category { get; set; }
    public string Label { get; set; } = "";

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set => SetField(ref _isChecked, value);
    }
}

public class SettingsViewModel : ObservableObject
{
    /// <summary>外置登录常用公共服务器 —— LittleSkin 的 Yggdrasil API 地址。</summary>
    public const string LittleSkinServerUrl = "https://littleskin.cn/api/yggdrasil";

    // ---- 启动 ----
    private string _gamePath = "";
    private string _javaPath = "";
    private ObservableCollection<JavaChoice> _detectedJavas = new();
    private int _maxMemoryMb = 2048;

    private string _username = "Player";
    private string _extraJvmArgs = "";
    private string _selectedRepairPolicy = "Ask";
    private string _selectedJavaVendor = "Auto";
    private string _selectedAutoInstallMods = "Ask";

    // ---- 通用 ----
    private string _selectedLanguage = "zh_CN";
    private bool _autoStartLauncher;
    private bool _minimizeToTray;
    private bool _animationsEnabled = true;
    private bool _fileWatchEnabled = true;

    // ---- 启动补充 ----
    private bool _prewarmEnabled;
    private bool _hudEnabled;
    private bool _launchCompatCheckEnabled = true;

    // ---- 下载 ----
    private string _selectedDownloadSource = "MirrorFirst";
    private int _maxConcurrentDownloads = 8;
    private bool _autoRepairResourcePacks = true;
    private bool _serverPackCacheEnabled = true;

    // ---- CurseForge（设置 → 下载）----
    private bool _curseForgeEnabled = true;
    private string _curseForgeApiKey = "";
    private string _curseForgeApiRoot = "";

    // ---- 推荐 ----
    private string _selectedIntelliRecommend = "Enabled";
    private ObservableCollection<CategoryPref> _categoryPreferences = new();

    // ---- AI 助手（独立页面 AiSettingsView 持有，逻辑见 AiSettingsViewModel）----
    private readonly AiSettingsViewModel _aiVm = new();

    // ---- 外观 ----
    private string _selectedTheme = "Dark";
    private string _themeColor = "#3a7b4f";
    private string _backgroundImagePath = "";
    private double _fontScale = 1.0;
    private bool _highDpiEnabled;
    private bool _showControlBorders = true;

    // ---- 关于 / 更新 ----
    private bool _autoUpdateCheck = true;
    private string _updateMessage = "";
    private string _launcherVersion = GameConstants.LauncherVersion;

    // ---- 账号 ----
    private ObservableCollection<AccountEntry> _accounts = new();
    private AccountEntry? _selectedAccount;
    private string _newOfflineName = "";
    private string _authlibServerUrl = "";
    private string _authlibEmail = "";
    private string _authlibPassword = "";
    private string _microsoftOAuthClientId = "";

    private string _statusMessage = "";

    // ===== 启动 =====

    /// <summary>Minecraft 游戏目录（.minecraft），可自定义（bug #26）。留空表示使用系统默认。</summary>
    public string GamePath
    {
        get => _gamePath;
        set => SetField(ref _gamePath, value);
    }

    /// <summary>游戏目录输入框的水印提示，显示系统默认路径。</summary>
    public string DefaultGamePathHint => GameConstants.SystemGameRoot;

    private int _toastDurationSeconds = 5;
    public int ToastDurationSeconds { get => _toastDurationSeconds; set => SetField(ref _toastDurationSeconds, value); }

    private bool _seasonalEffectsEnabled = true;
    /// <summary>是否启用节日特效置顶叠加层（关闭后立即移除叠加层、恢复默认外观）。</summary>
    public bool SeasonalEffectsEnabled { get => _seasonalEffectsEnabled; set => SetField(ref _seasonalEffectsEnabled, value); }

    public string JavaPath { get => _javaPath; set => SetField(ref _javaPath, value); }
    /// <summary>本机可用 Java 下拉清单（已过 <see cref="JavaValidator"/> 交叉校验）。</summary>
    public ObservableCollection<JavaChoice> DetectedJavas { get => _detectedJavas; set => SetField(ref _detectedJavas, value); }
    public int MaxMemoryMb { get => _maxMemoryMb; set => SetField(ref _maxMemoryMb, value); }

    public string Username { get => _username; set => SetField(ref _username, value); }
    public string ExtraJvmArgs { get => _extraJvmArgs; set => SetField(ref _extraJvmArgs, value); }
    public string SelectedRepairPolicy { get => _selectedRepairPolicy; set => SetField(ref _selectedRepairPolicy, value); }
    public string SelectedJavaVendor { get => _selectedJavaVendor; set => SetField(ref _selectedJavaVendor, value); }
    public string SelectedAutoInstallMods { get => _selectedAutoInstallMods; set => SetField(ref _selectedAutoInstallMods, value); }

    /// <summary>启动预热开关（规格 2.4 — 启动）。Off → false，Light/Full → true。</summary>
    public bool PrewarmEnabled
    {
        get => _prewarmEnabled;
        set => SetField(ref _prewarmEnabled, value);
    }

    /// <summary>HUD 叠加开关（规格 2.4 — 启动）。</summary>
    public bool HudEnabled
    {
        get => _hudEnabled;
        set { if (SetField(ref _hudEnabled, value)) ApplyHudLive(); }
    }

    // ===== HUD 可配置化（清单 #54）=====
    // 此前 HUD 只有「开 / 关」一个开关，HudConfig 里的字段、锚点、字号、不透明度、刷新间隔
    // 全部无法编辑，且保存设置时会被 new HudConfig { Enabled = ... } 整体覆盖掉——
    // 现在全部暴露到设置页，并即时套用到已打开的 HUD 窗口。

    /// <summary>HUD 字段勾选项（勾选变化直接回调 VM，触发即时套用）。</summary>
    public class HudFieldOption
    {
        public HudField Field { get; init; }
        public string Name { get; init; } = "";
        public Action? OnChanged { get; init; }
        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                OnChanged?.Invoke();
            }
        }
    }

    public ObservableCollection<HudFieldOption> HudFields { get; } = new();

    private string _hudAnchorKey = "TopLeft";
    private int _hudX, _hudY;
    private int _hudFontSize = 12;
    private double _hudOpacity = 0.75;
    private int _hudRefreshMs = 1000;
    private int _hudMargin = 12;
    private bool _hudClickThrough = true;
    private bool _hudOnlyWhenForeground;
    private bool _hudShowBackground = true;

    /// <summary>HUD 停靠位置（字符串键，界面下拉用；Custom 由拖动 HUD 产生）。</summary>
    public string HudAnchorKey
    {
        get => _hudAnchorKey;
        set { if (SetField(ref _hudAnchorKey, value)) ApplyHudLive(); }
    }

    public int HudFontSize
    {
        get => _hudFontSize;
        set { if (SetField(ref _hudFontSize, Math.Clamp(value, 8, 32))) ApplyHudLive(); }
    }

    public double HudOpacity
    {
        get => _hudOpacity;
        set { if (SetField(ref _hudOpacity, Math.Clamp(value, 0.1, 1.0))) ApplyHudLive(); }
    }

    public int HudRefreshMs
    {
        get => _hudRefreshMs;
        set { if (SetField(ref _hudRefreshMs, Math.Clamp(value, 200, 5000))) ApplyHudLive(); }
    }

    public int HudMargin
    {
        get => _hudMargin;
        set { if (SetField(ref _hudMargin, Math.Clamp(value, 0, 64))) ApplyHudLive(); }
    }

    public bool HudClickThrough
    {
        get => _hudClickThrough;
        set { if (SetField(ref _hudClickThrough, value)) ApplyHudLive(); }
    }

    public bool HudOnlyWhenForeground
    {
        get => _hudOnlyWhenForeground;
        set { if (SetField(ref _hudOnlyWhenForeground, value)) ApplyHudLive(); }
    }

    public bool HudShowBackground
    {
        get => _hudShowBackground;
        set { if (SetField(ref _hudShowBackground, value)) ApplyHudLive(); }
    }

    /// <summary>清单 #63：全局快捷键开关（Ctrl+Alt+Enter 快速启动 / Ctrl+Alt+M 呼出启动器）。</summary>
    public bool GlobalHotkeysEnabled
    {
        get => _globalHotkeysEnabled;
        set
        {
            if (!SetField(ref _globalHotkeysEnabled, value)) return;
            try
            {
                Chert.App.Services.GlobalHotkeyService.Detach();
                if (value && System.Windows.Application.Current.MainWindow is System.Windows.Window w)
                    Chert.App.Services.GlobalHotkeyService.Attach(w);
            }
            catch { /* 热键注册失败不影响设置保存 */ }
        }
    }

    private bool _globalHotkeysEnabled = true;

    /// <summary>恢复 HUD 默认配置。</summary>
    public ICommand ResetHudCommand { get; }

    private void ResetHud()
    {
        var d = new HudConfig();
        HudAnchorKey = d.Anchor.ToString();
        HudFontSize = d.FontSize;
        HudOpacity = d.Opacity;
        HudRefreshMs = d.RefreshMs;
        HudMargin = d.Margin;
        HudClickThrough = d.ClickThrough;
        HudOnlyWhenForeground = d.OnlyWhenGameForeground;
        HudShowBackground = d.ShowBackground;
        foreach (var f in HudFields) f.IsChecked = d.Has(f.Field);
        ApplyHudLive();
        StatusMessage = "已恢复 HUD 默认配置";
    }

    /// <summary>按当前设置项构造 HUD 配置（保留上次拖动得到的自定义坐标）。</summary>
    private HudConfig BuildHudConfig()
    {
        var fields = HudField.None;
        foreach (var o in HudFields)
            if (o.IsChecked) fields |= o.Field;

        return new HudConfig
        {
            Enabled = HudEnabled,
            Anchor = Enum.TryParse<HudAnchor>(HudAnchorKey, out var a) ? a : HudAnchor.TopLeft,
            X = _hudX,
            Y = _hudY,
            Margin = HudMargin,
            Opacity = HudOpacity,
            FontSize = HudFontSize,
            // 一个字段都不勾时至少显示帧率，避免 HUD 空白
            Fields = fields == HudField.None ? HudField.Fps : fields,
            RefreshMs = HudRefreshMs,
            OnlyWhenGameForeground = HudOnlyWhenForeground,
            ClickThrough = HudClickThrough,
            ShowBackground = HudShowBackground
        };
    }

    /// <summary>即时套用到已打开的 HUD（不落盘；落盘在保存设置时进行）。</summary>
    private void ApplyHudLive()
    {
        try { Chert.App.Views.HudOverlayWindow.Instance?.ApplyConfig(BuildHudConfig()); }
        catch { /* HUD 未打开或出错时忽略 */ }
    }

    private static string HudFieldKey(HudField f) => f switch
    {
        HudField.Fps => "settings.hud_field_fps",
        HudField.Memory => "settings.hud_field_memory",
        HudField.Cpu => "settings.hud_field_cpu",
        HudField.Ping => "settings.hud_field_ping",
        HudField.Coordinates => "settings.hud_field_coords",
        HudField.Biome => "settings.hud_field_biome",
        HudField.GameTime => "settings.hud_field_gametime",
        HudField.SessionTime => "settings.hud_field_session",
        _ => "settings.hud_field_fps"
    };

    /// <summary>启动前存档兼容性检测（规格 2.4 — 启动）。</summary>
    public bool LaunchCompatCheckEnabled
    {
        get => _launchCompatCheckEnabled;
        set => SetField(ref _launchCompatCheckEnabled, value);
    }

    // ===== 通用 =====
    public string SelectedLanguage { get => _selectedLanguage; set => SetField(ref _selectedLanguage, value); }
    public bool AutoStartLauncher { get => _autoStartLauncher; set => SetField(ref _autoStartLauncher, value); }
    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set
        {
            if (SetField(ref _minimizeToTray, value))
            {
                // bug #9：即时落盘，避免「切换后未点保存就最小化」导致托盘不生效
                try
                {
                    var p = ProfileStore.Load(GameConstants.DefaultGameRoot);
                    p.MinimizeToTray = value;
                    ProfileStore.Save(p);
                }
                catch { /* 忽略持久化失败 */ }
            }
        }
    }
    public bool AnimationsEnabled
    {
        get => _animationsEnabled;
        set
        {
            if (SetField(ref _animationsEnabled, value))
                Chert.App.MainWindow.AnimationsEnabled = value;   // 即时生效，无需重启（bug #4）
        }
    }

    /// <summary>文件变更检测开关（规格 2.4 — 通用）。</summary>
    public bool FileWatchEnabled
    {
        get => _fileWatchEnabled;
        set => SetField(ref _fileWatchEnabled, value);
    }

    /// <summary>游戏启动时音乐自动降音量 / 暂停（规格 2.3 面板 14，可配置）。代理到音乐播放器单例。</summary>
    public bool MusicAutoDuck
    {
        get => MusicPlayerViewModel.Instance.AutoDuck;
        set => MusicPlayerViewModel.Instance.AutoDuck = value;
    }

    /// <summary>启动器启动时自动续播上次音乐（bug #10）。代理到音乐播放器单例。</summary>
    public bool MusicResumeOnLaunch
    {
        get => MusicPlayerViewModel.Instance.ResumeOnLaunch;
        set => MusicPlayerViewModel.Instance.ResumeOnLaunch = value;
    }

    // ===== 本地客户端模式（规格 · 设置项定义）=====
    // 四项都直接读写 profile 里的 MusicClient，并经 Normalized() 兜住
    // 「配置被手改 / 旧版残留 / 跨设备同步」带来的越界与非法枚举值。

    /// <summary>总开关：关闭时完全不涉及外部客户端进程（规格联动规则：后三项随之不可用）。</summary>
    public bool ClientModeEnabled
    {
        get => LoadClientPrefs().Enabled;
        set
        {
            var p = LoadClientPrefs();
            if (p.Enabled == value) return;
            p.Enabled = value;
            SaveClientPrefs(p);
            OnPropertyChanged();
            // 联动：后三项的可用性跟着总开关走
            OnPropertyChanged(nameof(ClientGraceMinutes));
            OnPropertyChanged(nameof(ClientGraceText));
            OnPropertyChanged(nameof(LyricPinModeIndex));
            OnPropertyChanged(nameof(ClientModeLyricEnabled));
            MusicPlayerViewModel.Instance.OnClientPrefsChanged();
        }
    }

    /// <summary>客户端宽限期（分钟，0–30）。0 = 不自动关闭。</summary>
    public int ClientGraceMinutes
    {
        get => LoadClientPrefs().GraceMinutes;
        set
        {
            var p = LoadClientPrefs();
            // 先钳到规格区间再判等 —— 用户在滑块上拖出越界值时不应反复写盘
            var clamped = Math.Clamp(value, MusicClientPrefs.MinGraceMinutes, MusicClientPrefs.MaxGraceMinutes);
            if (p.GraceMinutes == clamped) return;
            p.GraceMinutes = clamped;
            SaveClientPrefs(p);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ClientGraceText));
            MusicPlayerViewModel.Instance.OnClientPrefsChanged();
        }
    }

    /// <summary>宽限期的展示文本（含单位，随开关状态变化）。</summary>
    public string ClientGraceText
    {
        get
        {
            var p = LoadClientPrefs();
            var m = p.GraceMinutes;
            return m == 0 ? "0" : $"{m} 分钟";
        }
    }

    /// <summary>
    /// 歌词固定方式（枚举下标，供 ComboBox 双向绑定）。
    /// <para>只存下标、不存枚举名 —— ComboBox 的 ItemsSource 顺序变了也不会读错。</para>
    /// </summary>
    public int LyricPinModeIndex
    {
        get => (int)LoadClientPrefs().LyricPin;
        set
        {
            var p = LoadClientPrefs();
            if (!Enum.IsDefined(typeof(LyricPinMode), value)) return;   // 非法下标忽略
            var mode = (LyricPinMode)value;
            if (p.LyricPin == mode) return;
            p.LyricPin = mode;
            SaveClientPrefs(p);
            OnPropertyChanged();
            MusicPlayerViewModel.Instance.OnClientPrefsChanged();
        }
    }

    /// <summary>客户端模式下仍用 API 获取歌词（关闭则隐藏歌词区）。</summary>
    public bool ClientModeLyricEnabled
    {
        get => LoadClientPrefs().LyricEnabled;
        set
        {
            var p = LoadClientPrefs();
            if (p.LyricEnabled == value) return;
            p.LyricEnabled = value;
            SaveClientPrefs(p);
            OnPropertyChanged();
            MusicPlayerViewModel.Instance.OnClientPrefsChanged();
        }
    }

    /// <summary>已选客户端程序路径（代理播放器单例，设置页只读展示）。</summary>
    public string ClientExePath => MusicPlayerViewModel.Instance.ClientExePath;

    /// <summary>选择客户端程序（代理播放器单例，走同一个文件选择与持久化）。</summary>
    public ICommand BrowseClientCommand => MusicPlayerViewModel.Instance.BrowseClientCommand;

    /// <summary>读本地客户端模式设置（读失败回落到默认值，绝不让设置页打不开）。</summary>
    private static MusicClientPrefs LoadClientPrefs()
    {
        try
        {
            return ProfileStore.Load(LauncherService.Instance.GameRoot).MusicClient.Normalized();
        }
        catch
        {
            return new MusicClientPrefs();
        }
    }

    /// <summary>写回本地客户端模式设置。</summary>
    private static void SaveClientPrefs(MusicClientPrefs prefs)
    {
        try
        {
            var p = ProfileStore.Load(LauncherService.Instance.GameRoot);
            p.MusicClient = prefs;
            ProfileStore.Save(p);
        }
        catch
        {
            // 持久化失败不阻断设置页交互，用户仍能继续改其它项
        }
    }

    // ===== 在线音源（对接 MusicSourceMode.Api）=====
    // 五项都在自己的读写里即时 NotifyApiChanged()：切换数据源要求播放器立刻重建
    // provider 并重新判定登录态，否则用户改完地址还要重启才生效。

    private static void NotifyApiChanged() => MusicPlayerViewModel.Instance.OnApiPrefsChanged();

    private static MusicApiPrefs LoadApiPrefs()
    {
        try { return ProfileStore.Load(LauncherService.Instance.GameRoot).MusicApi.Normalized(); }
        catch { return new MusicApiPrefs(); }
    }

    private static void SaveApiPrefs(MusicApiPrefs prefs)
    {
        try
        {
            var p = ProfileStore.Load(LauncherService.Instance.GameRoot);
            p.MusicApi = prefs;
            ProfileStore.Save(p);
        }
        catch { /* 写盘失败不阻断设置页交互 */ }
    }

    /// <summary>总开关：关闭后音乐页不再显示「在线」入口。</summary>
    public bool ApiEnabled
    {
        get => LoadApiPrefs().Enabled;
        set
        {
            var p = LoadApiPrefs();
            if (p.Enabled == value) return;
            p.Enabled = value;
            SaveApiPrefs(p);
            OnPropertyChanged();
            NotifyApiChanged();
        }
    }

    /// <summary>
    /// 数据源（下拉下标）：0 = Meting 聚合（免登录），1 = 厂家自建 API（可扫码登录）。
    /// <para>枚举里仍保留 <c>NeteaseApi</c> 只为兼容旧配置，界面不再单独暴露它 ——
    /// 它与「厂家自建 API + 网易云」完全等价，留两个入口只会让用户困惑该选哪个。</para>
    /// <para>切到 Meting 会<b>连带重置服务地址</b>：两套协议的请求形态完全不同，
    /// 沿用旧地址必然得到一串解析失败。</para>
    /// </summary>
    public int ApiKindIndex
    {
        get => LoadApiPrefs().Kind == MusicApiKind.Meting ? 0 : 1;
        set
        {
            var p = LoadApiPrefs();
            var kind = value == 0 ? MusicApiKind.Meting : MusicApiKind.VendorApi;
            if (p.Kind == kind) return;

            p.Kind = kind;
            p.Normalized();
            if (kind == MusicApiKind.Meting)
                p.BaseUrl = MusicApiPrefs.DefaultMetingUrl;   // 厂家地址不能拿去问 Meting 实例

            SaveApiPrefs(p);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ApiBaseUrl));
            OnPropertyChanged(nameof(ApiPlatformVisible));
            OnPropertyChanged(nameof(ApiCanLogin));
            OnPropertyChanged(nameof(ApiVendorHint));
            OnPropertyChanged(nameof(ApiVendorLoginSupported));
            NotifyApiChanged();
        }
    }

    /// <summary>
    /// 服务根地址（不含结尾斜杠）。
    /// <para>Meting 模式下它是<b>聚合实例</b>的地址（一个实例服务所有厂家）；
    /// 厂家模式下它是<b>当前厂家自己那个服务</b>的地址 —— 两者含义不同，分开存取。</para>
    /// </summary>
    public string ApiBaseUrl
    {
        get
        {
            var p = LoadApiPrefs();
            return p.Kind == MusicApiKind.Meting ? p.BaseUrl : p.UrlFor(VendorPlatformOf(p));
        }
        set
        {
            var v = (value ?? "").Trim().TrimEnd('/');
            var p = LoadApiPrefs();

            if (p.Kind == MusicApiKind.Meting)
            {
                if (p.BaseUrl == v) return;
                p.BaseUrl = v;
            }
            else
            {
                var platform = VendorPlatformOf(p);
                if (p.UrlFor(platform) == v) return;
                p.SetUrl(platform, v);
            }

            SaveApiPrefs(p);
            OnPropertyChanged();
            NotifyApiChanged();
        }
    }

    /// <summary>
    /// 厂家 / 搜索范围（下拉下标，与 <see cref="MusicApiPlatform"/> 的数值顺序一致）。
    /// <para>「全部平台」只在 Meting 下有意义（并发搜多家再合并）；
    /// 厂家模式下选它会退回网易云 —— 登录这类动作必须落在具体厂家上。</para>
    /// </summary>
    public int ApiPlatformIndex
    {
        get => (int)LoadApiPrefs().Platform;
        set
        {
            if (!Enum.IsDefined(typeof(MusicApiPlatform), value)) return;
            var p = LoadApiPrefs();
            var v = (MusicApiPlatform)value;
            if (p.Platform == v) return;
            p.Platform = v;
            SaveApiPrefs(p);
            OnPropertyChanged();
            // 每家的服务地址与登录能力都不同，切厂家后这些展示必须跟着变
            OnPropertyChanged(nameof(ApiBaseUrl));
            OnPropertyChanged(nameof(ApiVendorHint));
            OnPropertyChanged(nameof(ApiVendorLoginSupported));
            OnPropertyChanged(nameof(ApiCanLogin));
            OnPropertyChanged(nameof(ApiLoggedIn));
            OnPropertyChanged(nameof(ApiAccountLabel));
            NotifyApiChanged();
        }
    }

    /// <summary>厂家下拉是否显示（两种数据源都要选厂家，故恒为 true）。</summary>
    public bool ApiPlatformVisible => true;

    /// <summary>
    /// 「真正要对话的那个厂家」。
    /// <para>「全部平台」只是搜索范围，登录、服务地址这类动作必须落到具体厂家 ——
    /// 这里统一退回网易云（自建项目最成熟的一家），与 <see cref="OnlineMusicService"/> 的取法保持一致，
    /// 否则界面显示的地址会和实际请求用的地址不是同一个。</para>
    /// </summary>
    private static MusicApiPlatform VendorPlatformOf(MusicApiPrefs p) =>
        p.Platform == MusicApiPlatform.All ? MusicApiPlatform.Netease : p.Platform;

    /// <summary>
    /// 当前厂家的自建 API 说明（建议部署的项目 + 注意事项）。
    /// 没有可用自建项目的厂家会明确告知「已退回 Meting 聚合」，而不是让用户对着一个连不上的输入框猜。
    /// </summary>
    public string ApiVendorHint
    {
        get
        {
            var p = LoadApiPrefs();
            if (p.Kind == MusicApiKind.Meting) return "";

            var profile = VendorApiProfiles.For(VendorPlatformOf(p));
            return profile is null
                ? "该厂家暂无可用的自建 API 项目，已自动退回 Meting 聚合（免登录，搜索与播放仍可用）。"
                : "建议部署：" + profile.Project + "。" + profile.Notes;
        }
    }

    /// <summary>当前厂家是否真的能扫码登录（酷我 / 百度 / 虾米没有可用项目）。</summary>
    public bool ApiVendorLoginSupported
    {
        get
        {
            var p = LoadApiPrefs();
            if (p.Kind == MusicApiKind.Meting) return false;
            return VendorApiProfiles.CanLogin(VendorPlatformOf(p));
        }
    }

    /// <summary>在线播放音质。</summary>
    public int ApiQualityIndex
    {
        get => (int)LoadApiPrefs().Quality;
        set
        {
            if (!Enum.IsDefined(typeof(MusicApiQuality), value)) return;
            var p = LoadApiPrefs();
            var v = (MusicApiQuality)value;
            if (p.Quality == v) return;
            p.Quality = v;
            SaveApiPrefs(p);
            OnPropertyChanged();
            NotifyApiChanged();
        }
    }

    public bool ApiCanLogin => MusicPlayerViewModel.Instance.CanOnlineLogin;

    public bool ApiLoggedIn => MusicPlayerViewModel.Instance.IsOnlineLoggedIn;

    public string ApiAccountLabel => MusicPlayerViewModel.Instance.ApiAccountLabel;

    /// <summary>把地址恢复为当前协议推荐的默认值。</summary>
    public ICommand ApiRestoreDefaultUrlCommand { get; }

    public ICommand ApiLoginCommand => MusicPlayerViewModel.Instance.StartApiLoginCommand;
    public ICommand ApiLogoutCommand => MusicPlayerViewModel.Instance.ApiLogoutCommand;
    public ICommand ApiCancelLoginCommand => MusicPlayerViewModel.Instance.CancelApiLoginCommand;

    /// <summary>
    /// 播放器单例。设置页需要直接绑定它的二维码 / 登录状态
    /// （这些状态属于播放器会话，不属于设置项，没必要再代理一层）。
    /// </summary>
    public MusicPlayerViewModel MusicPlayer => MusicPlayerViewModel.Instance;

    // ===== 下载 =====
    public string SelectedDownloadSource { get => _selectedDownloadSource; set => SetField(ref _selectedDownloadSource, value); }
    public int MaxConcurrentDownloads { get => _maxConcurrentDownloads; set => SetField(ref _maxConcurrentDownloads, value); }

    /// <summary>进服时自动修复资源包问题（规格 2.4 — 下载）。</summary>
    public bool AutoRepairResourcePacks
    {
        get => _autoRepairResourcePacks;
        set => SetField(ref _autoRepairResourcePacks, value);
    }

    /// <summary>服务器资源包缓存开关（规格 2.4 — 下载）。关闭时进服不缓存资源包。</summary>
    public bool ServerPackCacheEnabled
    {
        get => _serverPackCacheEnabled;
        set => SetField(ref _serverPackCacheEnabled, value);
    }

    // ---- CurseForge 接入（设置 → 下载）----

    /// <summary>CurseForge 源总开关。关闭后下载中心的 CurseForge 来源不可选，Modrinth 不受影响。</summary>
    public bool CurseForgeEnabled
    {
        get => _curseForgeEnabled;
        set { if (SetField(ref _curseForgeEnabled, value)) OnPropertyChanged(nameof(CurseForgeStatusText)); }
    }

    /// <summary>用户自定义 API Key（留空 = 使用构建时注入的内置 Key）。</summary>
    public string CurseForgeApiKey
    {
        get => _curseForgeApiKey;
        set { if (SetField(ref _curseForgeApiKey, value)) OnPropertyChanged(nameof(CurseForgeStatusText)); }
    }

    /// <summary>API Root 覆盖（留空 = 官方 https://api.curseforge.com；可填第三方镜像）。</summary>
    public string CurseForgeApiRoot
    {
        get => _curseForgeApiRoot;
        set { if (SetField(ref _curseForgeApiRoot, value)) OnPropertyChanged(nameof(CurseForgeStatusText)); }
    }

    /// <summary>当前 Key 来源提示（内置 / 自定义 / 未配置 / 已关闭）。</summary>
    public string CurseForgeStatusText
    {
        get
        {
            if (!CurseForgeEnabled) return LocaleManager.T("settings.cf_status_disabled");
            if (!string.IsNullOrWhiteSpace(CurseForgeApiKey)) return LocaleManager.T("settings.cf_status_user");
            return CurseForgeConfig.BuiltInApiKey.Length > 0
                ? LocaleManager.T("settings.cf_status_builtin")
                : LocaleManager.T("settings.cf_status_none");
        }
    }

    // ===== 推荐 =====
    public string SelectedIntelliRecommend { get => _selectedIntelliRecommend; set => SetField(ref _selectedIntelliRecommend, value); }
    public ObservableCollection<CategoryPref> CategoryPreferences { get => _categoryPreferences; set => SetField(ref _categoryPreferences, value); }

    // ===== AI 助手（独立页面，绑定到 AiSettingsView）=====
    public AiSettingsViewModel AiSettingsVM => _aiVm;

    // ===== 外观 =====
    public string SelectedTheme { get => _selectedTheme; set => SetField(ref _selectedTheme, value); }

    /// <summary>新建版本默认隔离模式（bug2.txt #9，绑定通用设置 ComboBox）。</summary>
    private IsolationMode _defaultVersionIsolation = IsolationMode.Auto;
    public IsolationMode DefaultVersionIsolation
    {
        get => _defaultVersionIsolation;
        set => SetField(ref _defaultVersionIsolation, value);
    }
    private string _windowBackgroundColor = "";
    private bool _glassBlurEnabled = true;
    private bool _hideSidebarLabels;
    private double _glassOpacity = 75;

    /// <summary>清单 #17：外观页——窗口背景色（空 = 跟随主题）。</summary>
    public string WindowBackgroundColor
    {
        get => _windowBackgroundColor;
        set
        {
            if (!SetField(ref _windowBackgroundColor, value ?? "")) return;
            Chert.App.App.ApplyWindowBackgroundColor(string.IsNullOrWhiteSpace(value) ? null : value);
        }
    }

    /// <summary>清单 #17：外观页——是否启用系统级毛玻璃背板。</summary>
    public bool GlassBlurEnabled
    {
        get => _glassBlurEnabled;
        set
        {
            if (!SetField(ref _glassBlurEnabled, value)) return;
            ThemeManager.GlassBlurEnabled = value;
            // 立刻重算外壳：玻璃风格下这一步决定要不要向系统申请背板
            Chert.App.App.ApplyUiStyle(UiStyles.Parse(_uiStyle));
        }
    }

    /// <summary>
    /// 清单 #17：外观页——毛玻璃不透明度（30–100，百分比）。
    /// 数值越大外壳越实、越小越透（桌面虚化越明显）；作用范围见 MainWindow.GlassAlpha。
    /// 用 double 而不是 int：Slider.Value 是 double，绑到 int 会触发转换与绑定错误日志。
    /// </summary>
    public double GlassOpacity
    {
        get => _glassOpacity;
        set
        {
            var v = Math.Clamp(value, 30, 100);
            if (!SetField(ref _glassOpacity, v)) return;
            ThemeManager.GlassOpacity = v / 100.0;
            // 实时预览：立刻重算外壳（拖滑块即可看到变化）
            ThemeManager.NotifyAppearanceChanged();
        }
    }

    /// <summary>清单 #17：外观页——隐藏侧边栏图标标签。</summary>
    public bool HideSidebarLabels
    {
        get => _hideSidebarLabels;
        set
        {
            if (!SetField(ref _hideSidebarLabels, value)) return;
            ThemeManager.HideSidebarLabels = value;
            ThemeManager.NotifyAppearanceChanged();
        }
    }

    /// <summary>清单 #17：外观页的 8 个预设主题色点（对齐 design/chert_layout.html 的 .theme-dot）。</summary>
    public IReadOnlyList<ThemeSwatch> ThemeSwatches { get; } = new List<ThemeSwatch>
    {
        new("#3B82F6"), new("#10B981"), new("#8B5CF6"), new("#EC4899"),
        new("#F59E0B"), new("#EF4444"), new("#06B6D4"), new("#EAB308")
    };

    private ICommand? _pickThemeColor;

    /// <summary>点主题色点 → 直接设 ThemeColor。</summary>
    public ICommand PickThemeColorCommand =>
        _pickThemeColor ??= new RelayCommand(p => { if (p is string hex) ThemeColor = hex; });

    public string ThemeColor
    {
        get => _themeColor;
        set
        {
            if (SetField(ref _themeColor, value))
            {
                Chert.App.App.ApplyAccentColor(value);
                OnPropertyChanged(nameof(ThemeColorValue));
            }
        }
    }

    /// <summary>
    /// 清单 #59：主题色的 Color 形态。让主题色选择与皮肤编辑器共用同一个
    /// 全色域取色器控件（ColorPicker），两者 UI 与交互完全一致。
    /// </summary>
    public System.Windows.Media.Color ThemeColorValue
    {
        get => Chert.App.Controls.ColorMath.TryParseHex(_themeColor, out var c)
            ? c
            : System.Windows.Media.Colors.Black;
        set => ThemeColor = Chert.App.Controls.ColorMath.ToHex(value);
    }
    public string BackgroundImagePath
    {
        get => _backgroundImagePath;
        set
        {
            if (SetField(ref _backgroundImagePath, value))
                Chert.App.App.ApplyBackgroundImage(value); // 即时预览（bug #20）
        }
    }
    public double FontScale
    {
        get => _fontScale;
        set { if (SetField(ref _fontScale, value)) Chert.App.App.ApplyFontScale(value); }
    }

    /// <summary>适配高分辨率屏幕：开启后图标加载 2x 高清资源（规格 2.4 — 外观）。实时驱动 IconManager。</summary>
    public bool HighDpiEnabled
    {
        get => _highDpiEnabled;
        set
        {
            if (SetField(ref _highDpiEnabled, value))
                IconManager.HighDpi = value;
        }
    }

    /// <summary>显示控件边框：开/关实时驱动 App.ApplyControlBorders（透明=隐藏描边）。</summary>
    public bool ShowControlBorders
    {
        get => _showControlBorders;
        set
        {
            if (SetField(ref _showControlBorders, value))
                App.ApplyControlBorders(value);
        }
    }

    // ===== 关于 / 更新 =====
    public bool AutoUpdateCheck { get => _autoUpdateCheck; set => SetField(ref _autoUpdateCheck, value); }
    public string UpdateMessage { get => _updateMessage; set => SetField(ref _updateMessage, value); }
    public string LauncherVersion => _launcherVersion;

    // ===== 账号 =====
    public ObservableCollection<AccountEntry> Accounts { get => _accounts; set => SetField(ref _accounts, value); }
    public AccountEntry? SelectedAccount { get => _selectedAccount; set => SetField(ref _selectedAccount, value); }
    public string NewOfflineName { get => _newOfflineName; set => SetField(ref _newOfflineName, value); }
    public string AuthlibServerUrl { get => _authlibServerUrl; set => SetField(ref _authlibServerUrl, value); }
    public string AuthlibEmail { get => _authlibEmail; set => SetField(ref _authlibEmail, value); }
    public string AuthlibPassword { get => _authlibPassword; set => SetField(ref _authlibPassword, value); }
    public string MicrosoftOAuthClientId { get => _microsoftOAuthClientId; set => SetField(ref _microsoftOAuthClientId, value); }

    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    public List<string> AvailableLanguages => LocaleManager.AvailableLocales;
    public ICommand SaveCommand { get; }
    public ICommand AutoDetectJavaCommand { get; }
    public ICommand RefreshAccountsCommand { get; }
    public ICommand SetActiveAccountCommand { get; }
    public ICommand AddOfflineAccountCommand { get; }
    /// <summary>一键填入 LittleSkin 服务器地址（外置登录）。</summary>
    public ICommand UseLittleSkinCommand { get; }
    public ICommand RemoveAccountCommand { get; }
    public ICommand BrowseBackgroundCommand { get; }
    public ICommand BrowseGameRootCommand { get; }
    public ICommand OpenGameRootCommand { get; }
    public ICommand ResetGameRootCommand { get; }
    public ICommand CheckUpdateCommand { get; }
    public ICommand LoginMicrosoftCommand { get; }
    public ICommand TestCurseForgeCommand { get; }


    public SettingsViewModel()
    {
        ApiRestoreDefaultUrlCommand = new RelayCommand(_ =>
        {
            var p = LoadApiPrefs();
            ApiBaseUrl = MusicApiPrefs.DefaultUrlFor(p.Kind, VendorPlatformOf(p));
        });
        SaveCommand = new RelayCommand(_ => Save());
        AutoDetectJavaCommand = new AsyncRelayCommand(_ => AutoDetectJavaAsync());
        RefreshAccountsCommand = new RelayCommand(_ => RefreshAccounts());
        SetActiveAccountCommand = new RelayCommand(_ => SetActiveAccount());
        AddOfflineAccountCommand = new RelayCommand(_ => AddOfflineAccount());
        UseLittleSkinCommand = new RelayCommand(_ => UseLittleSkin());
        ResetHudCommand = new RelayCommand(_ => ResetHud());
        EditTouchLayoutCommand = new RelayCommand(_ => EditTouchLayout());
        ResetTouchLayoutCommand = new RelayCommand(_ => ResetTouchLayout());
        RemoveAccountCommand = new RelayCommand(p => RemoveAccount(p as AccountEntry));
        BrowseBackgroundCommand = new RelayCommand(_ => BrowseBackground());
        BrowseGameRootCommand = new RelayCommand(_ => BrowseGameRoot());
        OpenGameRootCommand = new RelayCommand(_ => OpenGameRoot());
        ResetGameRootCommand = new RelayCommand(_ => ResetGameRoot());
        CheckUpdateCommand = new AsyncRelayCommand(_ => CheckUpdateAsync());
        LoginMicrosoftCommand = new AsyncRelayCommand(_ => LoginMicrosoftAsync());
        TestCurseForgeCommand = new AsyncRelayCommand(_ => TestCurseForgeAsync());


        var profile = ProfileStore.Load(GameConstants.DefaultGameRoot);
        LoadFromProfile(profile);
        _aiVm.Load(profile);
        RefreshAccounts();

        // 主题/语言偏好
        ThemeManager.LoadPreference(GameConstants.DefaultGameRoot);
        _selectedTheme = ThemeManager.Current.ToString();
        // 清单 #18：跟随系统主题
        _followSystemTheme = ThemeManager.FollowSystem;
        _manualThemeEnabled = !_followSystemTheme;
        OnPropertyChanged(nameof(FollowSystemTheme));
        OnPropertyChanged(nameof(ManualThemeEnabled));
        _selectedLanguage = LocaleManager.CurrentLocale;
        // 清单 #12：界面风格（standard / android / glass）
        _uiStyle = UiStyles.ToId(UiStyles.Parse(ThemeManager.UiStyle));

        // 同步运行时 AI 配置
        Assistant.Config = profile.Ai ?? new AiConfig();

        // 后台刷新 Ollama 安装/服务/已拉取模型状态（不阻塞界面）
        _ = _aiVm.RefreshStatusAsync();
    }

    private void LoadFromProfile(LauncherProfile profile)
    {
        // 游戏目录以启动器级配置为准（bug #26）：未自定义时留空，输入框显示水印默认路径
        GamePath = GameConstants.IsGameRootCustomized ? GameConstants.DefaultGameRoot : "";
        JavaPath = profile.JavaPath ?? "";
        // 首次打开设置即把可用 Java 列进下拉：该操作较慢且失败无碍，故后台执行不阻塞界面
        _ = LoadValidatedJavasAsync();
        MaxMemoryMb = profile.MaxMemoryMb;
        Username = profile.DefaultUsername;
        ExtraJvmArgs = string.Join(" ", profile.ExtraJvmArgs);
        SelectedRepairPolicy = profile.RepairPolicy.ToString();
        SelectedJavaVendor = profile.PreferredJavaVendor.ToString();
        SelectedAutoInstallMods = profile.AutoInstallMissingMods.ToString();
        SelectedIntelliRecommend = profile.IntelliRecommend.ToString();
        LoadCategoryPreferences(profile);

        // 通用
        SelectedLanguage = profile.Language;
        AutoStartLauncher = profile.AutoStartLauncher;
        MinimizeToTray = profile.MinimizeToTray;
        AnimationsEnabled = profile.AnimationsEnabled;
        FileWatchEnabled = profile.FileWatchEnabled;
        ToastDurationSeconds = profile.ToastDurationSeconds;
        SeasonalEffectsEnabled = profile.SeasonalEffectsEnabled;
        // 清单 #11：触屏模式
        _touch = profile.Touch ?? TouchControlConfig.CreateDefault();
        _touchEnabled = _touch.Enabled;
        // 「跟随游戏窗口」三项直接读 _touch，加载后要通知，否则 UI 上开关/单选/间距
        // 停留在绑定前的默认值（看似「设置没生效」）。
        OnPropertyChanged(nameof(TouchFollowGameWindow));
        OnPropertyChanged(nameof(TouchFollowLeftSide));
        OnPropertyChanged(nameof(TouchFollowRightSide));
        OnPropertyChanged(nameof(TouchFollowMargin));
        OnPropertyChanged(nameof(TouchModeEnabled));
        OnPropertyChanged(nameof(TouchButtonCount));
        DefaultVersionIsolation = profile.DefaultVersionIsolation;

        // 启动补充
        PrewarmEnabled = profile.Prewarm.Mode != PrewarmMode.Off;
        HudEnabled = profile.Hud.Enabled;

        // HUD 可配置化（清单 #54）
        var hud = profile.Hud ?? new HudConfig();
        HudFields.Clear();
        foreach (var f in HudConfig.SelectableFields)
            HudFields.Add(new HudFieldOption { Field = f, Name = LocaleManager.T(HudFieldKey(f)), IsChecked = hud.Has(f), OnChanged = ApplyHudLive });
        _hudX = hud.X;
        _hudY = hud.Y;
        _hudAnchorKey = hud.Anchor.ToString();
        _hudFontSize = hud.FontSize;
        _hudOpacity = hud.Opacity;
        _hudRefreshMs = hud.RefreshMs;
        _hudMargin = hud.Margin;
        _hudClickThrough = hud.ClickThrough;
        _hudOnlyWhenForeground = hud.OnlyWhenGameForeground;
        _hudShowBackground = hud.ShowBackground;
        OnPropertyChanged(nameof(HudAnchorKey));
        OnPropertyChanged(nameof(HudFontSize));
        OnPropertyChanged(nameof(HudOpacity));
        OnPropertyChanged(nameof(HudRefreshMs));
        OnPropertyChanged(nameof(HudMargin));
        OnPropertyChanged(nameof(HudClickThrough));
        OnPropertyChanged(nameof(HudOnlyWhenForeground));
        OnPropertyChanged(nameof(HudShowBackground));
        LaunchCompatCheckEnabled = profile.LaunchCompatCheckEnabled;

        // 清单 #63：全局快捷键（加载时只赋值不触发重注册）
        _globalHotkeysEnabled = profile.GlobalHotkeysEnabled;
        OnPropertyChanged(nameof(GlobalHotkeysEnabled));

        // 下载
        SelectedDownloadSource = profile.DownloadSource.ToString();
        MaxConcurrentDownloads = profile.MaxConcurrentDownloads;
        AutoRepairResourcePacks = profile.AutoRepairResourcePacks;
        ServerPackCacheEnabled = profile.ServerPackCacheEnabled;

        // CurseForge 接入
        CurseForgeEnabled = profile.CurseForge.Enabled;
        CurseForgeApiKey = profile.CurseForge.ApiKey ?? "";
        CurseForgeApiRoot = profile.CurseForge.ApiRoot ?? "";


        // 外观
        ThemeColor = profile.ThemeColor;
        WindowBackgroundColor = profile.WindowBackgroundColor ?? "";
        GlassBlurEnabled = profile.GlassBlurEnabled;
        GlassOpacity = Math.Clamp(profile.GlassOpacity, 30, 100);
        HideSidebarLabels = profile.HideSidebarLabels;
        BackgroundImagePath = profile.BackgroundImagePath ?? "";
        FontScale = profile.FontScale;
        HighDpiEnabled = profile.HighDpiIcons;
        ShowControlBorders = profile.ShowControlBorders;

        // 关于
        AutoUpdateCheck = profile.AutoUpdateCheck;

        // 账号
        MicrosoftOAuthClientId = profile.MicrosoftOAuthClientId ?? "";
    }

    private void LoadCategoryPreferences(LauncherProfile profile)
    {
        var prefs = new ObservableCollection<CategoryPref>();
        foreach (var cat in GameplayCategoryMap.All)
        {
            prefs.Add(new CategoryPref
            {
                Category = cat,
                Label = GameplayCategoryMap.DisplayName(cat),
                IsChecked = profile.PreferredCategories.Contains(cat)
            });
        }
        CategoryPreferences = prefs;
    }

    private bool _followSystemTheme;
    private bool _manualThemeEnabled = true;

    /// <summary>清单 #18：跟随操作系统的亮 / 暗自动切换。</summary>
    public bool FollowSystemTheme
    {
        get => _followSystemTheme;
        set
        {
            if (!SetField(ref _followSystemTheme, value)) return;
            ManualThemeEnabled = !value;
            ThemeManager.FollowSystem = value;
            if (value) Chert.App.Services.SystemThemeWatcher.ApplyFromSystem();
        }
    }

    /// <summary>开启跟随系统后，手动主题下拉框变为只读。</summary>
    public bool ManualThemeEnabled
    {
        get => _manualThemeEnabled;
        set => SetField(ref _manualThemeEnabled, value);
    }

    private string _uiStyle = "standard";

    /// <summary>
    /// 清单 #12 / #15 / #16：界面风格 Id（standard / android / glass）。
    /// 赋值即切换：App 层叠加对应风格资源字典，主窗口按风格切换沉底导航。
    /// </summary>
    private ICommand? _selectUiStyleCommand;

    /// <summary>外观页的布局卡片点击（等价于设置 <see cref="SelectedUiStyle"/>）。
    /// 用延迟初始化而不是字段初始化器 —— 后者不能引用实例属性（CS0236）。</summary>
    public ICommand SelectUiStyleCommand =>
        _selectUiStyleCommand ??= new RelayCommand(p => { if (p is string s) SelectedUiStyle = s; });

    public string SelectedUiStyle
    {
        get => _uiStyle;
        set
        {
            var v = value ?? "standard";
            if (!SetField(ref _uiStyle, v)) return;
            Chert.App.App.ApplyUiStyle(UiStyles.Parse(v));
            ReportUiStyleSwitched(v);
        }
    }

    /// <summary>切换界面风格后的即时反馈（对齐设计稿右下角那条「已切换到灵动布局」提示）。</summary>
    private static void ReportUiStyleSwitched(string id)
    {
        var key = id switch
        {
            "android" => "settings.ui_style_android",
            "glass" => "settings.ui_style_glass",
            "dynamic" => "settings.ui_style_dynamic",
            _ => "settings.ui_style_standard"
        };
        ToastService.Show(
            LocaleManager.T("settings.appearance"),
            $"{LocaleManager.T("settings.ui_style_switched")} {LocaleManager.T(key)}",
            ToastKind.Info);
    }

    // ===== 清单 #11：触屏模式 =====

    private bool _touchEnabled;
    private TouchControlConfig _touch = TouchControlConfig.CreateDefault();

    /// <summary>触屏模式总开关：开启后随游戏进程显示虚拟按键面板。</summary>
    public bool TouchModeEnabled
    {
        get => _touchEnabled;
        set
        {
            if (!SetField(ref _touchEnabled, value)) return;
            _touch.Enabled = value;
            OnPropertyChanged(nameof(TouchButtonCount));
            try { Chert.App.Views.TouchOverlayWindow.ApplyConfig(_touch); } catch { }
        }
    }

    /// <summary>当前布局中的虚拟按键数量（展示用）。</summary>
    public int TouchButtonCount => _touch.Buttons.Count;

    // ★ 以下三项为「跟随游戏窗口」配置（problem3）。关闭时面板维持既有行为：
    //   固定尺寸 + 自由拖动 + 记忆坐标；开启后自动贴游戏窗口左/右边缘并实时跟随。
    //   每次改动都即时 ApplyConfig，热更新到已打开的面板（与 TouchModeEnabled 同款）。

    /// <summary>是否让触屏面板跟随游戏窗口（自动贴左/右边缘）。</summary>
    public bool TouchFollowGameWindow
    {
        get => _touch.FollowGameWindow;
        set
        {
            if (_touch.FollowGameWindow == value) return;
            _touch.FollowGameWindow = value;
            OnPropertyChanged();
            ApplyTouchConfig();
        }
    }

    /// <summary>跟随时贴合左侧（true）/ 右侧（false）。</summary>
    public bool TouchFollowLeftSide
    {
        get => _touch.FollowGameWindowLeftSide;
        set
        {
            if (_touch.FollowGameWindowLeftSide == value) return;
            _touch.FollowGameWindowLeftSide = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TouchFollowRightSide));   // 两个 RadioButton 互斥
            ApplyTouchConfig();
        }
    }

    /// <summary>跟随时贴合右侧（= !<see cref="TouchFollowLeftSide"/>）。</summary>
    public bool TouchFollowRightSide
    {
        get => !_touch.FollowGameWindowLeftSide;
        set { if (value) TouchFollowLeftSide = false; }
    }

    /// <summary>跟同时与游戏窗口边缘的水平间距（像素），钳位 0-200。</summary>
    public int TouchFollowMargin
    {
        get => _touch.FollowGameWindowMargin;
        set
        {
            var v = Math.Clamp(value, 0, 200);
            if (_touch.FollowGameWindowMargin == v) return;
            _touch.FollowGameWindowMargin = v;
            OnPropertyChanged();
            ApplyTouchConfig();
        }
    }

    /// <summary>把当前触屏配置热更新到已打开的面板并落盘（非关键，失败静默）。</summary>
    private void ApplyTouchConfig()
    {
        try { Chert.App.Views.TouchOverlayWindow.ApplyConfig(_touch); } catch { }
    }

    /// <summary>打开触屏按键布局编辑器。</summary>
    public ICommand EditTouchLayoutCommand { get; }
    /// <summary>恢复触屏按键默认布局。</summary>
    public ICommand ResetTouchLayoutCommand { get; }

    private void EditTouchLayout()
    {
        try
        {
            var win = new Chert.App.Views.TouchLayoutEditorWindow
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };
            win.ShowDialog();
            // 编辑器内已保存并 ApplyConfig，这里同步回 VM 状态
            var cfg = Chert.Core.Profiles.ProfileStore.Load(Chert.Core.Utils.GameConstants.DefaultGameRoot).Touch;
            if (cfg is not null)
            {
                _touch = cfg;
                _touchEnabled = cfg.Enabled;
                OnPropertyChanged(nameof(TouchModeEnabled));
                OnPropertyChanged(nameof(TouchButtonCount));
                OnPropertyChanged(nameof(TouchFollowGameWindow));
                OnPropertyChanged(nameof(TouchFollowLeftSide));
                OnPropertyChanged(nameof(TouchFollowRightSide));
                OnPropertyChanged(nameof(TouchFollowMargin));
            }
        }
        catch { /* 非关键 */ }
    }

    private void ResetTouchLayout()
    {
        var d = TouchControlConfig.CreateDefault();
        d.Enabled = _touchEnabled;
        _touch = d;
        OnPropertyChanged(nameof(TouchButtonCount));
        try { Chert.App.Views.TouchOverlayWindow.ApplyConfig(d); } catch { }
    }

    // ===== 主题 / 语言 即时生效 =====

    public void ApplyTheme()
    {
        if (Enum.TryParse<ThemeType>(SelectedTheme, out var t))
        {
            ThemeManager.Current = t;
            // 清单 #18：FollowSystem 与当前主题一起落盘
            ThemeManager.FollowSystem = FollowSystemTheme;
            ThemeManager.SavePreference(GameConstants.DefaultGameRoot);
        }
    }

    public void ApplyLanguage()
    {
        LocaleManager.CurrentLocale = SelectedLanguage;
    }

    /// <summary>
    /// 把「本设置页不负责」的字段从磁盘上的旧配置搬回新构造的 profile。
    /// <para>
    /// 背景：<see cref="Save"/> 里是 <c>new LauncherProfile { ... }</c>，只列出本页负责的字段，
    /// 其余字段会被**默认值覆盖**。实测有 26 个字段会在每次「保存设置」时静默丢失：
    /// 窗口布局记忆、音乐自动续播 / 音量 / 断点、收藏的挂机工作流与光影 Token、
    /// 下载限速与自动重试、局域网联动、备份策略、上次游玩版本与账号、分辨率、最小内存、
    /// 四色标签自定义配色、侧边栏配置……
    /// </para>
    /// <para>
    /// 「启动时自动续播」的开关本身就绑在本页，但它的 setter 由 <c>MusicPlayerViewModel</c>
    /// 立即落盘，而随后的保存又会把它重置 —— 这正是用户反馈「需要持久化」的原因。
    /// </para>
    /// <para>AI 配置不在此列：它由 <c>_aiVm.ApplyTo(profile)</c> 另行写回。</para>
    /// </summary>
    private static void RestoreUntouchedFields(LauncherProfile profile)
    {
        try
        {
            var old = ProfileStore.Load(GameConstants.DefaultGameRoot);

            // 运行时状态：上次游玩版本 / 上次账号 / 分辨率 / 窗口布局记忆
            profile.MinMemoryMb = old.MinMemoryMb;
            profile.LastVersionId = old.LastVersionId;
            profile.LastAccountId = old.LastAccountId;
            profile.ResolutionWidth = old.ResolutionWidth;
            profile.ResolutionHeight = old.ResolutionHeight;
            profile.WindowLeft = old.WindowLeft;
            profile.WindowTop = old.WindowTop;
            profile.WindowWidth = old.WindowWidth;
            profile.WindowHeight = old.WindowHeight;
            profile.WindowMaximized = old.WindowMaximized;

            // 下载补充项（限速 / 自动重试）
            profile.DownloadSpeedLimitKbps = old.DownloadSpeedLimitKbps;
            profile.DownloadAutoRetryCount = old.DownloadAutoRetryCount;

            // 其它模块负责的配置
            profile.TabTheme = old.TabTheme;
            profile.Sidebar = old.Sidebar;
            profile.LanLink = old.LanLink;
            profile.Backup = old.Backup;
            profile.ServerPackCacheMb = old.ServerPackCacheMb;

            // 收藏内容（挂机工作流 / 光影配置 Token）
            profile.AfkWorkflows = old.AfkWorkflows;
            profile.ShaderTokens = old.ShaderTokens;

            // 音乐播放器（setter 已即时落盘，这里搬回磁盘值即可）
            profile.MusicAutoDuck = old.MusicAutoDuck;
            profile.MusicVolume = old.MusicVolume;
            profile.MusicResumeOnLaunch = old.MusicResumeOnLaunch;
            profile.MusicLastTrack = old.MusicLastTrack;
            profile.MusicLastPosition = old.MusicLastPosition;
            profile.MusicLastFolder = old.MusicLastFolder;
            profile.MusicClient = old.MusicClient;
            profile.MusicClientExePath = old.MusicClientExePath;
        }
        catch
        {
            // 读不到旧配置（首次运行 / 文件损坏）就按默认值走，不影响保存本身
        }
    }

    private void Save()
    {
        // 游戏目录可能是用户手输的，先应用再写 profile，保证 profile 落到正确的目录里（bug #26）
        var typed = string.IsNullOrWhiteSpace(GamePath) ? null : GamePath.Trim();
        if (!string.Equals(typed ?? GameConstants.SystemGameRoot,
                           GameConstants.DefaultGameRoot, StringComparison.OrdinalIgnoreCase))
            ApplyGameRoot(typed);

        var profile = new LauncherProfile
        {
            JavaPath = string.IsNullOrWhiteSpace(JavaPath) ? null : JavaPath,
            MaxMemoryMb = MaxMemoryMb,
            DefaultUsername = Username,
            GameRoot = GameConstants.DefaultGameRoot,
            ExtraJvmArgs = string.IsNullOrWhiteSpace(ExtraJvmArgs)
                ? new List<string>()
                : ExtraJvmArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(),
            RepairPolicy = Enum.TryParse<CrashRepairPolicy>(SelectedRepairPolicy, out var rp) ? rp : CrashRepairPolicy.Ask,
            PreferredJavaVendor = Enum.TryParse<JavaVendor>(SelectedJavaVendor, out var jv) ? jv : JavaVendor.Auto,
            AutoInstallMissingMods = Enum.TryParse<AutoInstallPolicy>(SelectedAutoInstallMods, out var ai) ? ai : AutoInstallPolicy.Ask,
            IntelliRecommend = Enum.TryParse<IntelliRecommendMode>(SelectedIntelliRecommend, out var ir) ? ir : IntelliRecommendMode.Enabled,
            PreferredCategories = CategoryPreferences.Where(p => p.IsChecked).Select(p => p.Category).ToList(),

            // 通用
            Language = SelectedLanguage,
            AutoStartLauncher = AutoStartLauncher,
            MinimizeToTray = MinimizeToTray,
            AnimationsEnabled = AnimationsEnabled,
            FileWatchEnabled = FileWatchEnabled,
            ToastDurationSeconds = ToastDurationSeconds,
            SeasonalEffectsEnabled = SeasonalEffectsEnabled,
            DefaultVersionIsolation = DefaultVersionIsolation,

            // 启动补充（规格 2.4）
            Prewarm = new PrewarmConfig { Mode = PrewarmEnabled ? PrewarmMode.Light : PrewarmMode.Off },
            // 清单 #54：保留完整 HUD 配置（此前会被 new HudConfig 覆盖掉除开关外的一切）
            Hud = BuildHudConfig(),
            // 清单 #11：保留完整触屏按键布局（含坐标 / 大小 / 键码）
            Touch = _touch,
            LaunchCompatCheckEnabled = LaunchCompatCheckEnabled,

            // 清单 #63
            GlobalHotkeysEnabled = GlobalHotkeysEnabled,

            // 下载
            DownloadSource = Enum.TryParse<DownloadSourcePreference>(SelectedDownloadSource, out var ds) ? ds : DownloadSourcePreference.MirrorFirst,
            MaxConcurrentDownloads = MaxConcurrentDownloads,
            AutoRepairResourcePacks = AutoRepairResourcePacks,
            ServerPackCacheEnabled = ServerPackCacheEnabled,

            // CurseForge 接入（Key / API Root / 开关）
            CurseForge = new CurseForgeSettings
            {
                Enabled = CurseForgeEnabled,
                ApiKey = CurseForgeApiKey?.Trim() ?? "",
                ApiRoot = CurseForgeApiRoot?.Trim() ?? ""
            },

            // 外观
            ThemeColor = ThemeColor,
            WindowBackgroundColor = string.IsNullOrWhiteSpace(WindowBackgroundColor) ? null : WindowBackgroundColor,
            GlassBlurEnabled = GlassBlurEnabled,
            GlassOpacity = (int)Math.Round(GlassOpacity),
            HideSidebarLabels = HideSidebarLabels,
            BackgroundImagePath = string.IsNullOrWhiteSpace(BackgroundImagePath) ? null : BackgroundImagePath,
            FontScale = FontScale,
            HighDpiIcons = HighDpiEnabled,
            ShowControlBorders = ShowControlBorders,

            // 关于 / 更新
            AutoUpdateCheck = AutoUpdateCheck,

            // 账号
            MicrosoftOAuthClientId = MicrosoftOAuthClientId?.Trim() ?? "",

        };

        // ⚠️ 上面 new 出来的是**全新** profile：本页不负责的字段会被重置成默认值。
        // 必须把磁盘上的旧值搬回来，否则每次点「保存设置」都会静默丢配置（实测 26 个字段）。
        RestoreUntouchedFields(profile);

        _aiVm.ApplyTo(profile);
        ProfileStore.Save(profile);
        Chert.App.Services.ToastService.DurationSeconds = profile.ToastDurationSeconds;
        Chert.App.Themes.SeasonalThemeManager.Enabled = profile.SeasonalEffectsEnabled;
        // 开机自启：开关此前只落库未生效，这里同步 HKCU\\Run 注册表项
        Chert.App.Services.AutoStartService.Apply(profile.AutoStartLauncher);

        // 即时生效：把 CurseForge 配置同步进 Core，无需重启
        LauncherService.Instance.ApplyCurseForgeSettings();
        OnPropertyChanged(nameof(CurseForgeStatusText));

        // 即时生效
        ApplyTheme();
        App.ApplyBackgroundImage(profile.BackgroundImagePath); // 保存后确保背景图片生效（bug #20）
        ApplyLanguage();
        Assistant.Config = profile.Ai;

        StatusMessage = "已保存设置";
    }

    private async Task AutoDetectJavaAsync()
    {
        var javas = await LoadValidatedJavasAsync();
        if (javas.Count == 0)
        {
            StatusMessage = "未检测到可用的 Java（不可用的安装已自动排除）";
            return;
        }

        // 列表已按版本降序：优先满足启动器最低要求，其次退回最高可用版本
        var required = GameConstants.MinimumJavaMajorVersion;
        var best = javas.FirstOrDefault(j => j.MajorVersion >= required) ?? javas.FirstOrDefault();
        if (best is null) return;

        JavaPath = best.JavaExe;
        StatusMessage = $"找到 {javas.Count} 个可用 Java，已选择 {JavaValidator.Describe(best)}";
    }

    /// <summary>
    /// 扫描并交叉校验本机 Java，刷新下拉清单（不改动当前选择）。
    /// 当前已保存的路径即便未被扫到也会补进清单，避免设置「显示不出来」。
    /// </summary>
    private async Task<List<JavaInfo>> LoadValidatedJavasAsync()
    {
        // 校验并发解析全部 java -version 输出，耗时较长，放在后台线程；不可用的候选在此被剔除
        var javas = await Task.Run(() => JavaValidator.DetectValidatedAsync());

        var choices = javas.Select(j => new JavaChoice
        {
            Key = j.JavaExe,
            Display = JavaValidator.Describe(j)
        }).ToList();

        var current = JavaPath?.Trim();
        if (!string.IsNullOrWhiteSpace(current)
            && !choices.Any(c => string.Equals(c.Key, current, StringComparison.OrdinalIgnoreCase)))
            choices.Insert(0, new JavaChoice { Key = current, Display = $"当前设置 · {current}" });

        var view = new ObservableCollection<JavaChoice>(choices);
        if (Application.Current is not null)
            await Application.Current.Dispatcher.InvokeAsync(() => DetectedJavas = view);
        else
            DetectedJavas = view;

        return javas;
    }

    // ===== 账号 =====
    private void RefreshAccounts()
    {
        Accounts = new ObservableCollection<AccountEntry>(AccountStore.Load(GameConstants.DefaultGameRoot));
        var last = AccountStore.GetLastUsed(GameConstants.DefaultGameRoot);
        SelectedAccount = Accounts.FirstOrDefault(a => a.Id == last?.Id) ?? Accounts.FirstOrDefault();
    }

    private void SetActiveAccount()
    {
        if (SelectedAccount is null) return;
        AccountStore.MarkUsed(GameConstants.DefaultGameRoot, SelectedAccount.Id);
        // 用 AuthTypeText（中文）而非 AuthType（authlib / offline 这类技术词）
        StatusMessage = $"当前账号: {SelectedAccount.DisplayName}（{SelectedAccount.SubtitleText}）";
    }

    private void AddOfflineAccount()
    {
        if (string.IsNullOrWhiteSpace(NewOfflineName)) { StatusMessage = "请填写离线用户名"; return; }
        var root = GameConstants.DefaultGameRoot;
        var session = new OfflineAuthenticator().AuthenticateAsync(NewOfflineName).GetAwaiter().GetResult();
        var entry = new AccountEntry
        {
            DisplayName = NewOfflineName,
            AuthType = "offline",
            Username = session.Username,
            Uuid = session.Uuid
        };
        // bug2.txt #3 同名规避：已存在同名离线账号则复用其 Id（覆盖刷新），避免重复添加
        AccountEntry? offlineHit = null;
        foreach (var a in AccountStore.Load(root))
            if (a.AuthType == "offline" && a.Username == session.Username) { offlineHit = a; break; }
        if (offlineHit is not null) entry.Id = offlineHit.Id;
        AccountStore.Upsert(root, entry);
        NewOfflineName = "";
        RefreshAccounts();
        StatusMessage = $"已添加离线账号：{session.Username}";
    }

    /// <summary>
    /// 测试 CurseForge 连接：先把当前界面上的 Key / API Root 同步进 Core，再发一次最小请求验证。
    /// 仅测试，不落盘（用户仍需点保存）。
    /// </summary>
    private async Task TestCurseForgeAsync()
    {
        StatusMessage = LocaleManager.T("settings.cf_testing");
        try
        {
            CurseForgeConfig.LaunchArgumentOverride = null;
            CurseForgeConfig.UserApiKey = string.IsNullOrWhiteSpace(CurseForgeApiKey) ? null : CurseForgeApiKey.Trim();
            CurseForgeConfig.ApiRoot = CurseForgeApiRoot?.Trim() ?? "";
            CurseForgeConfig.Enabled = true;

            var client = new Chert.Core.Download.CurseForgeClient(LauncherService.Instance.ApiClient);
            var ok = await client.TestConnectionAsync();

            StatusMessage = ok
                ? LocaleManager.T("settings.cf_test_ok")
                : LocaleManager.Tf("settings.cf_test_fail", client.LastError ?? LocaleManager.T("settings.cf_test_unknown"));
            OnPropertyChanged(nameof(CurseForgeStatusText));
        }
        catch (Exception ex)
        {
            StatusMessage = LocaleManager.Tf("settings.cf_test_fail", ex.Message);
        }
    }

    /// <summary>填入 LittleSkin 公共服务器地址，省去手抄 URL。</summary>
    private void UseLittleSkin()
    {
        AuthlibServerUrl = LittleSkinServerUrl;
        StatusMessage = $"已填入 LittleSkin 服务器地址：{LittleSkinServerUrl}";
    }

    /// <summary>添加 Authlib-Injector 账号（由视图读取密码后调用）。</summary>
    public async Task AddAuthlibAccount(string serverUrl, string email, string password)
    {
        if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(email))
        {
            StatusMessage = "请填写服务器地址与邮箱";
            return;
        }
        try
        {
            // 必须异步等待：Authlib 认证含网络往返，使用 .GetAwaiter().GetResult() 会在 UI 线程上
            // 同步阻塞导致界面卡死（bug：配置外置登录时异常卡死）。
            StatusMessage = "Authlib 登录中…";
            var auth = new AuthlibInjectorAuthenticator(new HttpClient(), serverUrl, email, password);
            var session = await auth.AuthenticateAsync(email);
            var entry = new AccountEntry
            {
                DisplayName = session.Username,
                AuthType = "authlib",
                Username = session.Username,
                Uuid = session.Uuid,
                AccessToken = session.AccessToken,
                AuthlibServerUrl = serverUrl
            };
            // bug2.txt #3 同名规避：同服务器+同邮箱已存在则复用 Id（覆盖刷新令牌）
            AccountEntry? authlibHit = null;
            foreach (var a in AccountStore.Load(GameConstants.DefaultGameRoot))
                if (a.AuthType == "authlib" && a.Username == session.Username && a.AuthlibServerUrl == serverUrl) { authlibHit = a; break; }
            if (authlibHit is not null) entry.Id = authlibHit.Id;
            AccountStore.Upsert(GameConstants.DefaultGameRoot, entry);
            AuthlibServerUrl = "";
            AuthlibEmail = "";
            AuthlibPassword = "";
            RefreshAccounts();
            StatusMessage = $"已添加 Authlib 账号：{session.Username}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Authlib 登录失败：{ex.Message}";
        }
    }

    private void CopyMicrosoftDeviceCode(string msg)
    {
        try
        {
            var m = System.Text.RegularExpressions.Regex.Match(msg, @"输入代码：(\S+)");
            if (!m.Success) return;

            var code = m.Groups[1].Value.Trim();

            // 此前是「静默复制」——剪贴板里明明有了，界面上却什么都没说，
            // 用户不知道可以直接到浏览器粘贴（反馈：没有设备码已写入剪贴板的提示）。
            // 统一派发到 UI 线程后再改状态 / 弹 Toast，避免非 UI 线程触碰绑定属性。
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                System.Windows.Clipboard.SetText(code);
                var tip = LocaleManager.Tf("settings.ms_code_copied", code);
                StatusMessage = tip;
                ToastService.Show(LocaleManager.T("settings.ms_login_title"), tip, ToastKind.Success);
            });
        }
        catch { /* 剪贴板不可用时忽略 */ }
    }

    private async Task LoginMicrosoftAsync()
    {
        try
        {
            // 设备代码流无需用户手动粘贴：启动器申请设备码 → 弹窗展示验证码 → 打开浏览器 → 后台轮询换取令牌。
            // client_id 留空则使用内置默认（官方启动器 client_id）。
            var clientId = MicrosoftOAuthClientId?.Trim();
            StatusMessage = "正在发起微软登录…请按弹窗提示在浏览器输入设备代码。";
            var auth = new MicrosoftAuthenticator(new HttpClient(), clientId,
                msg => { UIService.ShowMessage(msg, "微软登录"); CopyMicrosoftDeviceCode(msg); });
            var session = await auth.AuthenticateAsync(null);
            var entry = new AccountEntry
            {
                DisplayName = session.Username,
                AuthType = "microsoft",
                Username = session.Username,
                Uuid = session.Uuid,
                AccessToken = session.AccessToken
            };
            // bug2.txt #3 同名规避：同 UUID 微软账号已存在则复用 Id（刷新令牌），避免重复添加
            AccountEntry? msHit = null;
            foreach (var a in AccountStore.Load(GameConstants.DefaultGameRoot))
                if (a.AuthType == "microsoft" && a.Uuid == session.Uuid) { msHit = a; break; }
            if (msHit is not null) entry.Id = msHit.Id;
            AccountStore.Upsert(GameConstants.DefaultGameRoot, entry);
            RefreshAccounts();
            StatusMessage = $"已添加微软账号：{session.Username}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"微软登录失败：{ex.Message}";
        }
    }

    private void RemoveAccount(AccountEntry? account)
    {
        if (account is null) return;
        if (UIService.Confirm($"确定删除账号 {account.DisplayName}？", "删除账号"))
        {
            AccountStore.Remove(GameConstants.DefaultGameRoot, account.Id);
            RefreshAccounts();
            StatusMessage = $"已删除账号：{account.DisplayName}";
        }
    }

    private void BrowseBackground()
    {
        var path = UIService.PickFile("图片|*.png;*.jpg;*.jpeg;*.bmp", "选择背景图片");
        if (!string.IsNullOrEmpty(path)) BackgroundImagePath = path;
    }

    // ===== 游戏目录（bug #26）=====

    /// <summary>选择 Minecraft 游戏目录；选中后立即生效并持久化。</summary>
    private void BrowseGameRoot()
    {
        var path = UIService.PickFolder("选择 Minecraft 游戏目录（.minecraft）");
        if (string.IsNullOrWhiteSpace(path)) return;
        ApplyGameRoot(path);
    }

    /// <summary>恢复为系统默认目录 %APPDATA%\.minecraft。</summary>
    private void ResetGameRoot() => ApplyGameRoot(null);

    /// <summary>在资源管理器中打开当前游戏目录。</summary>
    private void OpenGameRoot()
    {
        var dir = string.IsNullOrWhiteSpace(GamePath) ? GameConstants.SystemGameRoot : GamePath;
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开目录失败：{ex.Message}";
        }
    }

    /// <summary>应用游戏目录：持久化 → 重建 LauncherService → 回填界面。传 null 恢复默认。</summary>
    private void ApplyGameRoot(string? path)
    {
        try
        {
            GameConstants.SetGameRoot(path);
            var effective = GameConstants.DefaultGameRoot;
            GamePath = GameConstants.IsGameRootCustomized ? effective : "";
            LauncherService.Reinitialize(effective);
            StatusMessage = GameConstants.IsGameRootCustomized
                ? $"游戏目录已切换到：{effective}"
                : $"已恢复默认游戏目录：{effective}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"设置游戏目录失败：{ex.Message}";
        }
    }

    private async Task CheckUpdateAsync()
    {
        var result = await UpdateNotifier.CheckAndShowAsync();
        if (!string.IsNullOrEmpty(result.Error))
            UpdateMessage = $"检查更新失败：{result.Error}";
        else if (result.Available)
        {
            var statusNote = result.Status == "emgent" ? "，紧急更新请尽快安装" : (result.Mandatory ? "，建议立即更新" : "");
            UpdateMessage = $"发现新版本 {result.LatestVersion}（当前 {result.CurrentVersion}）{statusNote} · singlefile 包已发布（GitHub Pages 可查更新，包托管于 CNB Release）";
        }
        else
            UpdateMessage = $"已是最新版本（{result.CurrentVersion}）";
    }
}

/// <summary>外观页主题色点（清单 #17）。</summary>
public sealed class ThemeSwatch
{
    public ThemeSwatch(string hex)
    {
        Hex = hex;
        var c = Chert.App.Controls.ColorMath.TryParseHex(hex, out var parsed)
            ? parsed : System.Windows.Media.Colors.Gray;
        SwatchBrush = new System.Windows.Media.SolidColorBrush(c);
    }

    public string Hex { get; }
    public System.Windows.Media.Brush SwatchBrush { get; }
}
