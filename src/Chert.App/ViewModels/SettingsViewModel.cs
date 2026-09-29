using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Chert.Core.Ai;
using Chert.Core.Auth;
using Chert.Core.Hud;
using Chert.Core.Launcher;
using Chert.Core.Localization;
using Chert.Core.Mvvm;
using Chert.Core.Profiles;
using Chert.Core.Recommend;
using Chert.Core.Theme;
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


    public SettingsViewModel()
    {
        SaveCommand = new RelayCommand(_ => Save());
        AutoDetectJavaCommand = new AsyncRelayCommand(_ => AutoDetectJavaAsync());
        RefreshAccountsCommand = new RelayCommand(_ => RefreshAccounts());
        SetActiveAccountCommand = new RelayCommand(_ => SetActiveAccount());
        AddOfflineAccountCommand = new RelayCommand(_ => AddOfflineAccount());
        UseLittleSkinCommand = new RelayCommand(_ => UseLittleSkin());
        ResetHudCommand = new RelayCommand(_ => ResetHud());
        RemoveAccountCommand = new RelayCommand(p => RemoveAccount(p as AccountEntry));
        BrowseBackgroundCommand = new RelayCommand(_ => BrowseBackground());
        BrowseGameRootCommand = new RelayCommand(_ => BrowseGameRoot());
        OpenGameRootCommand = new RelayCommand(_ => OpenGameRoot());
        ResetGameRootCommand = new RelayCommand(_ => ResetGameRoot());
        CheckUpdateCommand = new AsyncRelayCommand(_ => CheckUpdateAsync());
        LoginMicrosoftCommand = new AsyncRelayCommand(_ => LoginMicrosoftAsync());


        var profile = ProfileStore.Load(GameConstants.DefaultGameRoot);
        LoadFromProfile(profile);
        _aiVm.Load(profile);
        RefreshAccounts();

        // 主题/语言偏好
        ThemeManager.LoadPreference(GameConstants.DefaultGameRoot);
        _selectedTheme = ThemeManager.Current.ToString();
        _selectedLanguage = LocaleManager.CurrentLocale;

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


        // 外观
        ThemeColor = profile.ThemeColor;
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

    // ===== 主题 / 语言 即时生效 =====

    public void ApplyTheme()
    {
        if (Enum.TryParse<ThemeType>(SelectedTheme, out var t))
        {
            ThemeManager.Current = t;
            ThemeManager.SavePreference(GameConstants.DefaultGameRoot);
        }
    }

    public void ApplyLanguage()
    {
        LocaleManager.CurrentLocale = SelectedLanguage;
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
            LaunchCompatCheckEnabled = LaunchCompatCheckEnabled,

            // 清单 #63
            GlobalHotkeysEnabled = GlobalHotkeysEnabled,

            // 下载
            DownloadSource = Enum.TryParse<DownloadSourcePreference>(SelectedDownloadSource, out var ds) ? ds : DownloadSourcePreference.MirrorFirst,
            MaxConcurrentDownloads = MaxConcurrentDownloads,
            AutoRepairResourcePacks = AutoRepairResourcePacks,
            ServerPackCacheEnabled = ServerPackCacheEnabled,

            // 外观
            ThemeColor = ThemeColor,
            BackgroundImagePath = string.IsNullOrWhiteSpace(BackgroundImagePath) ? null : BackgroundImagePath,
            FontScale = FontScale,
            HighDpiIcons = HighDpiEnabled,
            ShowControlBorders = ShowControlBorders,

            // 关于 / 更新
            AutoUpdateCheck = AutoUpdateCheck,

            // 账号
            MicrosoftOAuthClientId = MicrosoftOAuthClientId?.Trim() ?? "",

        };
        _aiVm.ApplyTo(profile);
        ProfileStore.Save(profile);
        Chert.App.Services.ToastService.DurationSeconds = profile.ToastDurationSeconds;
        Chert.App.Themes.SeasonalThemeManager.Enabled = profile.SeasonalEffectsEnabled;
        // 开机自启：开关此前只落库未生效，这里同步 HKCU\\Run 注册表项
        Chert.App.Services.AutoStartService.Apply(profile.AutoStartLauncher);

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
        StatusMessage = $"当前账号: {SelectedAccount.DisplayName} ({SelectedAccount.AuthType})";
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
            if (m.Success)
            {
                var code = m.Groups[1].Value.Trim();
                System.Windows.Application.Current.Dispatcher.Invoke(() => System.Windows.Clipboard.SetText(code));
            }
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
