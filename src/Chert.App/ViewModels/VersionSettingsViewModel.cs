using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using Chert.Core.Download;
using Chert.Core.Models;
using Chert.Core.Mods;
using Chert.Core.Mvvm;
using Chert.Core.Profiles;
using Chert.App.Services;

namespace Chert.App.ViewModels;

/// <summary>
/// 版本设置（对齐 MCLCS-Linux VersionSettingsViewModel）：覆盖 ① 基础信息 / ③ 模组加载器 /
/// ④ 隔离与工作目录 / ⑤ Java 与性能 / ⑥ 分辨率与窗口 / ⑦ 模组与资源包管理 / ⑧ 版本锁定 / ⑨ 账号绑定，
/// 并真实接入加载器安装与 Modrinth 模组管理。
/// <para>
/// 与 Linux 的差异（有意保留）：未保存过设置时，隔离模式按当前
/// <see cref="VersionIsolation"/> 标记回推（已隔离→Auto，否则→Shared），
/// 而不是一律默认 Auto —— 避免用户「打开就保存」把既有共享目录版本静默搬到 versions/&lt;id&gt;。
/// </para>
/// </summary>
public class VersionSettingsViewModel : ObservableObject
{
    private readonly string _gameRoot;
    private readonly string _versionId;
    private readonly HttpClient _http = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    private readonly IDownloader _downloader;

    public string VersionId => _versionId;
    public string VersionType { get; }
    public string BaseMcVersion => VersionProfileStore.BaseMcVersion(_gameRoot, _versionId);

    /// <summary>安装加载器后版本列表需要刷新时触发。</summary>
    public event Action? VersionsChanged;

    /// <summary>删除版本后请求关闭设置窗口。</summary>
    public event Action? RequestClose;

    // ---- ① 基础信息 ----
    private string _displayName = "";
    public string DisplayName { get => _displayName; set => SetField(ref _displayName, value); }

    // ---- ④ 隔离 / 游戏目录 ----
    private IsolationMode _isolation = IsolationMode.Shared;
    public IsolationMode Isolation
    {
        get => _isolation;
        set
        {
            if (!SetField(ref _isolation, value)) return;
            OnPropertyChanged(nameof(EffectiveGameDirDisplay));
            OnPropertyChanged(nameof(IsCustomDir));
            // 切换隔离模式时实时刷新 mods/资源包/光影列表，使其指向新目录
            RefreshInstalled();
        }
    }

    private string? _customGameDir;
    public string? CustomGameDir
    {
        get => _customGameDir;
        set
        {
            if (!SetField(ref _customGameDir, value)) return;
            OnPropertyChanged(nameof(EffectiveGameDirDisplay));
            RefreshInstalled();
        }
    }

    /// <summary>
    /// 有效工作目录：与启动器保持一致——自定义目录优先；选定自动隔离或版本实际已通过
    /// .mclcs-isolated marker 隔离时指向 versions/&lt;id&gt;，否则回落共享根目录。
    /// 关键修正：之前仅按 profile.Isolation 字段解析，当版本已隔离但 profile 标 Shared（孤儿 marker）
    /// 时误把展示/操作目录落到根游戏目录。
    /// </summary>
    public string EffectiveGameDirDisplay => ComputeEffectiveGameDir();

    private string ComputeEffectiveGameDir()
    {
        if (Isolation == IsolationMode.Custom && !string.IsNullOrWhiteSpace(CustomGameDir))
            return CustomGameDir!;
        if (Isolation == IsolationMode.Auto || VersionIsolation.IsIsolated(_gameRoot, _versionId))
            return Path.Combine(_gameRoot, "versions", _versionId);
        return _gameRoot;
    }

    /// <summary>是否处于「自定义目录」隔离模式（控制自定义目录输入框可见性）。</summary>
    public bool IsCustomDir => Isolation == IsolationMode.Custom;

    /// <summary>隔离模式下拉项（供 ComboBox 直接绑定，避免写死在 XAML 里）。</summary>
    public static IReadOnlyList<IsolationChoice> IsolationChoices { get; } = new[]
    {
        new IsolationChoice(IsolationMode.Shared, "共享（与 .minecraft 共用）"),
        new IsolationChoice(IsolationMode.Auto, "自动隔离（versions/<id>）"),
        new IsolationChoice(IsolationMode.Custom, "自定义目录（物理隔离）")
    };

    // ---- ③ 模组加载器 ----
    public ModLoaderKind DetectedLoader { get; }
    public string DetectedLoaderText =>
        DetectedLoader switch
        {
            ModLoaderKind.Fabric => "Fabric",
            ModLoaderKind.Forge => "Forge",
            ModLoaderKind.Quilt => "Quilt",
            ModLoaderKind.NeoForge => "NeoForge",
            _ => "原版（未安装加载器）"
        };
    public bool IsVanilla => DetectedLoader == ModLoaderKind.None;

    // ---- ⑤ Java 与性能 ----
    private string? _javaPath;
    public string? JavaPath { get => _javaPath; set => SetField(ref _javaPath, value); }
    private int? _maxMemoryMb;
    public int? MaxMemoryMb { get => _maxMemoryMb; set => SetField(ref _maxMemoryMb, value); }
    private int? _minMemoryMb;
    public int? MinMemoryMb { get => _minMemoryMb; set => SetField(ref _minMemoryMb, value); }
    private string _extraJvmArgsText = "";
    public string ExtraJvmArgsText { get => _extraJvmArgsText; set => SetField(ref _extraJvmArgsText, value); }

    // ---- ⑥ 分辨率与窗口 ----
    private int? _resolutionWidth;
    public int? ResolutionWidth { get => _resolutionWidth; set => SetField(ref _resolutionWidth, value); }
    private int? _resolutionHeight;
    public int? ResolutionHeight { get => _resolutionHeight; set => SetField(ref _resolutionHeight, value); }
    private bool _fullscreen;
    public bool Fullscreen { get => _fullscreen; set => SetField(ref _fullscreen, value); }

    // ---- ⑧ 版本锁定 ----
    private bool _locked;
    public bool Locked { get => _locked; set => SetField(ref _locked, value); }

    // ---- ⑨ 账户绑定 ----
    public ObservableCollection<AccountEntry> Accounts { get; } = new();
    private AccountEntry? _boundAccount;
    /// <summary>启动该版本时优先使用的账号；为空表示「跟随全局（最后使用）」。</summary>
    public AccountEntry? BoundAccount
    {
        get => _boundAccount;
        set => SetField(ref _boundAccount, value);
    }

    // ---- ⑦ 模组 / 资源包 / 光影 ----
    public enum ModTabKind { Mods, ResourcePacks, Shaders }
    private ModTabKind _modTab = ModTabKind.Mods;
    public ModTabKind ModTab
    {
        get => _modTab;
        set { if (SetField(ref _modTab, value)) { OnPropertyChanged(nameof(IsModsTab)); RefreshInstalled(); } }
    }
    /// <summary>当前是否在 Mods 分页（控制「检查更新」按钮可见性）。</summary>
    public bool IsModsTab => ModTab == ModTabKind.Mods;

    public static IReadOnlyList<ModTabChoice> ModTabChoices { get; } = new[]
    {
        new ModTabChoice(ModTabKind.Mods, "模组"),
        new ModTabChoice(ModTabKind.ResourcePacks, "资源包"),
        new ModTabChoice(ModTabKind.Shaders, "光影")
    };

    public ObservableCollection<InstalledItemViewModel> InstalledItems { get; } = new();
    public ObservableCollection<ModSearchHit> SearchResults { get; } = new();

    private string _searchQuery = "";
    public string SearchQuery { get => _searchQuery; set => SetField(ref _searchQuery, value); }
    private bool _busy;
    public bool Busy { get => _busy; set => SetField(ref _busy, value); }
    private string _status = "";
    public string Status { get => _status; set => SetField(ref _status, value); }

    public ICommand SaveCommand { get; }
    public ICommand BrowseCustomDirCommand { get; }
    public ICommand BrowseJavaCommand { get; }
    public ICommand ClearAccountBindingCommand { get; }
    public ICommand InstallLoaderCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand RefreshModsCommand { get; }
    public ICommand RemoveItemCommand { get; }
    public ICommand SearchModsCommand { get; }
    public ICommand AddModCommand { get; }
    public ICommand CheckUpdatesCommand { get; }
    public ICommand DeleteVersionCommand { get; }

    public VersionSettingsViewModel(string gameRoot, string versionId, string versionType)
    {
        _gameRoot = gameRoot;
        _versionId = versionId;
        VersionType = versionType;
        _downloader = new HttpDownloader(_http, 8, null);

        var saved = VersionProfileStore.HasProfile(gameRoot, versionId);
        var p = VersionProfileStore.Load(gameRoot, versionId);

        _displayName = p.DisplayName;
        // 实际隔离状态以 .mclcs-isolated marker 为准（与启动器 GameDirFor 一致）：
        // marker 存在 = 版本实际隔离，下拉以 Auto 回显，确保「展示/操作目录」与游戏实际运行目录一致；
        // marker 不存在时，未保存过设置则回落共享根，已保存则用已存的 Isolation 字段。
        var actuallyIsolated = VersionIsolation.IsIsolated(gameRoot, versionId);
        _isolation = actuallyIsolated ? IsolationMode.Auto
            : (saved ? p.Isolation : IsolationMode.Shared);
        _customGameDir = p.CustomGameDir;
        _javaPath = p.JavaPath;
        _maxMemoryMb = p.MaxMemoryMb;
        _minMemoryMb = p.MinMemoryMb;
        _extraJvmArgsText = string.Join("\n", p.ExtraJvmArgs);
        _resolutionWidth = p.ResolutionWidth;
        _resolutionHeight = p.ResolutionHeight;
        _fullscreen = p.Fullscreen;
        _locked = p.Locked;

        foreach (var a in AccountStore.Load(gameRoot))
            Accounts.Add(a);
        _boundAccount = string.IsNullOrWhiteSpace(p.BoundAccountId)
            ? null
            : Accounts.FirstOrDefault(a => a.Id == p.BoundAccountId);

        DetectedLoader = VersionProfileStore.DetectLoader(gameRoot, versionId);

        SaveCommand = new RelayCommand(_ => Save());
        BrowseCustomDirCommand = new RelayCommand(_ => BrowseCustomDir());
        BrowseJavaCommand = new RelayCommand(_ => BrowseJava());
        ClearAccountBindingCommand = new RelayCommand(_ => BoundAccount = null);
        InstallLoaderCommand = new AsyncRelayCommand(InstallLoaderAsync);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        RefreshModsCommand = new RelayCommand(_ => RefreshInstalled());
        RemoveItemCommand = new RelayCommand(RemoveItem);
        SearchModsCommand = new AsyncRelayCommand(_ => SearchModsAsync());
        AddModCommand = new AsyncRelayCommand(AddModAsync);
        CheckUpdatesCommand = new AsyncRelayCommand(_ => CheckUpdatesAsync());
        DeleteVersionCommand = new RelayCommand(_ => DeleteVersion());

        RefreshInstalled();
    }

    private string EffectiveDir => EffectiveGameDirDisplay;

    // ---- 持久化 ----
    public void Save()
    {
        var p = new VersionProfile
        {
            DisplayName = DisplayName.Trim(),
            Isolation = Isolation,
            CustomGameDir = CustomGameDir?.Trim(),
            JavaPath = JavaPath?.Trim(),
            MaxMemoryMb = MaxMemoryMb,
            MinMemoryMb = MinMemoryMb,
            ExtraJvmArgs = ExtraJvmArgsText
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            ResolutionWidth = ResolutionWidth,
            ResolutionHeight = ResolutionHeight,
            Fullscreen = Fullscreen,
            Locked = Locked,
            BoundAccountId = BoundAccount?.Id
        };
        VersionProfileStore.Save(_gameRoot, _versionId, p);
        VersionProfileStore.ApplyIsolation(_gameRoot, _versionId, p);
        Status = "已保存版本设置";
    }

    // ---- 删除版本 ----
    private void DeleteVersion()
    {
        var dir = Path.Combine(_gameRoot, "versions", _versionId);
        if (!Directory.Exists(dir))
        {
            Status = $"版本目录不存在：{dir}";
            return;
        }

        var result = MessageBox.Show(
            $"确定要永久删除版本「{_versionId}」吗？\n\n{dir}\n\n该操作不可恢复。",
            "删除版本", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            Directory.Delete(dir, recursive: true);
            // 版本目录已删除，profile.json 随目录一起移除
            Status = $"版本 {_versionId} 已删除";

            // 清理指向已删除版本的「最后选择」，否则快速启动下拉会尝试回显一个不存在的版本
            try
            {
                var p = ProfileStore.Load(_gameRoot);
                if (string.Equals(p.LastVersionId, _versionId, StringComparison.OrdinalIgnoreCase))
                {
                    p.LastVersionId = "";
                    ProfileStore.Save(p);
                }
            }
            catch { /* 清理失败不影响删除结果 */ }

            // 通知全局刷新（游戏页快速启动下拉等），删除后不再残留该版本
            LauncherService.RaiseVersionListChanged();

            // 事件回调放在删除成功后、各自独立 try/catch：某订阅者抛异常不得掩盖
            // 「版本已删除」的结果，也不该让未捕获异常冒泡到 UI 线程。
            try { VersionsChanged?.Invoke(); } catch { }
            try { RequestClose?.Invoke(); } catch { }
        }
        catch (Exception ex)
        {
            Status = $"删除失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 锁定守卫：若当前版本已锁定，提示并返回 true（调用方应中止改写操作）。
    /// 锁定只阻止「改写版本文件」（安装加载器 / 增删 Mod），不阻止启动游戏。
    /// </summary>
    private bool GuardLocked(string action)
    {
        if (!VersionProfileStore.IsLocked(_gameRoot, _versionId)) return false;
        var msg = $"版本「{_versionId}」已锁定，无法{action}。请先在「版本锁定」中关闭锁定。";
        Status = msg;
        ToastService.Show("版本已锁定", msg, ToastKind.Warning);
        return true;
    }

    // ---- ④ 目录 / Java 浏览 ----
    private void BrowseCustomDir()
    {
        var dir = UIService.PickFolder("选择该版本的游戏工作目录");
        if (!string.IsNullOrWhiteSpace(dir))
        {
            CustomGameDir = dir;
            Isolation = IsolationMode.Custom;
        }
    }

    private void BrowseJava()
    {
        var exe = UIService.PickFile("Java 可执行文件|java.exe|所有文件|*.*", "选择 java.exe");
        if (!string.IsNullOrWhiteSpace(exe)) JavaPath = exe;
    }

    // ---- ③ 安装加载器 ----
    private async Task InstallLoaderAsync(object? loader)
    {
        var name = loader as string;
        if (string.IsNullOrWhiteSpace(name)) return;
        if (GuardLocked($"安装 {name} 加载器")) return;

        Busy = true;
        Status = $"正在安装 {name}（基于 {BaseMcVersion}）…";
        try
        {
            var newId = await LauncherService.Instance.InstallVersionAsync(BaseMcVersion, name!, null, default);
            if (!string.IsNullOrEmpty(newId))
            {
                Status = $"{name} 安装完成：新实例 {newId}（请在版本列表切换到它）";
                VersionsChanged?.Invoke();
            }
            else
            {
                Status = $"{name} 安装失败";
            }
        }
        catch (Exception ex)
        {
            Status = $"安装 {name} 失败：{ex.Message}";
        }
        finally { Busy = false; }
    }

    // ---- ⑦ 打开文件夹 ----
    private void OpenFolder(object? kind)
    {
        var dir = EffectiveDir;
        var target = kind switch
        {
            "mods" => Path.Combine(dir, "mods"),
            "resourcepacks" => Path.Combine(dir, "resourcepacks"),
            "shaderpacks" => Path.Combine(dir, "shaderpacks"),
            "saves" => Path.Combine(dir, "saves"),
            _ => dir
        };
        try
        {
            Directory.CreateDirectory(target);
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        }
        catch { Status = $"无法打开文件夹：{target}"; }
    }

    // ---- ⑦ 已装列表 ----
    private void RefreshInstalled()
    {
        InstalledItems.Clear();
        var dir = EffectiveDir;
        switch (ModTab)
        {
            case ModTabKind.Mods:
                var mgr = new ModManager(dir, _http, _downloader);
                foreach (var m in mgr.ListInstalledMods())
                    InstalledItems.Add(new InstalledItemViewModel
                    {
                        FileName = m.FileName,
                        DisplayName = string.IsNullOrEmpty(m.Name) ? m.FileName : m.Name,
                        Subtitle = $"v{m.InstalledVersion} · {m.Loader}",
                        Kind = "mod"
                    });
                break;
            case ModTabKind.ResourcePacks:
                AddFolderItems(Path.Combine(dir, "resourcepacks"), "resourcepack");
                break;
            case ModTabKind.Shaders:
                AddFolderItems(Path.Combine(dir, "shaderpacks"), "shaderpack");
                break;
        }
    }

    private void AddFolderItems(string dir, string kind)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFileSystemEntries(dir).OrderBy(Path.GetFileName))
            InstalledItems.Add(new InstalledItemViewModel
            {
                FileName = Path.GetFileName(f),
                DisplayName = Path.GetFileName(f),
                Subtitle = Directory.Exists(f) ? "文件夹" : "压缩包",
                Kind = kind
            });
    }

    private void RemoveItem(object? fileName)
    {
        var name = fileName as string;
        if (string.IsNullOrEmpty(name)) return;
        if (GuardLocked("移除文件")) return;

        var dir = EffectiveDir;
        var target = ModTab switch
        {
            ModTabKind.Mods => Path.Combine(dir, "mods", name),
            ModTabKind.ResourcePacks => Path.Combine(dir, "resourcepacks", name),
            ModTabKind.Shaders => Path.Combine(dir, "shaderpacks", name),
            _ => Path.Combine(dir, name)
        };

        try
        {
            if (File.Exists(target)) File.Delete(target);
            else if (Directory.Exists(target)) Directory.Delete(target, true);
        }
        catch (Exception ex) { Status = $"删除失败：{ex.Message}"; }

        RefreshInstalled();
    }

    // ---- ⑦ 搜索 + 添加（Modrinth） ----
    private async Task SearchModsAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;
        Busy = true;
        Status = "正在搜索 Modrinth…";
        SearchResults.Clear();
        try
        {
            var type = ModTab switch
            {
                ModTabKind.ResourcePacks => ModrinthProjectType.ResourcePack,
                ModTabKind.Shaders => ModrinthProjectType.Shader,
                _ => ModrinthProjectType.Mod
            };
            var client = new ModrinthClient(_http);
            var result = await client.SearchAsync(SearchQuery, BaseMcVersion, LoaderFilter, type, limit: 25);
            foreach (var h in result.Hits)
                SearchResults.Add(new ModSearchHit
                {
                    ProjectId = h.ProjectId, Title = h.Title, Slug = h.Slug, IconUrl = h.IconUrl
                });
            Status = SearchResults.Count > 0 ? $"找到 {SearchResults.Count} 个结果" : "无匹配结果";
        }
        catch (Exception ex) { Status = $"搜索失败：{ex.Message}"; }
        finally { Busy = false; }
    }

    private async Task AddModAsync(object? param)
    {
        if (param is not ModSearchHit hit) return;
        if (GuardLocked("添加模组 / 资源包 / 光影")) return;

        Busy = true;
        Status = $"正在添加 {hit.Title}…";
        try
        {
            var client = new ModrinthClient(_http);
            var versions = await client.GetVersionsAsync(hit.ProjectId, default);
            if (versions.Count == 0) { Status = "无可用版本"; return; }

            // 优先选与基版本完全匹配的文件
            ModrinthFile? file = null;
            foreach (var v in versions)
            {
                var f = client.SelectBestFile(v, BaseMcVersion, LoaderFilter);
                if (f is not null) { file = f; break; }
            }
            file ??= client.SelectBestFile(versions[0], null, LoaderType.Any);
            if (file is null) { Status = "未找到可下载文件"; return; }

            var sub = ModTab switch
            {
                ModTabKind.ResourcePacks => "resourcepacks",
                ModTabKind.Shaders => "shaderpacks",
                _ => "mods"
            };
            var destDir = Path.Combine(EffectiveDir, sub);
            Directory.CreateDirectory(destDir);
            var dest = Path.Combine(destDir, file.FileName);
            await _downloader.DownloadAsync(new DownloadItem(new[] { file.Url }, dest, file.Hashes.Sha1), null, default);
            Status = $"已添加：{file.FileName}";
            RefreshInstalled();
        }
        catch (Exception ex) { Status = $"添加失败：{ex.Message}"; }
        finally { Busy = false; }
    }

    private async Task CheckUpdatesAsync()
    {
        if (ModTab != ModTabKind.Mods) { Status = "仅模组支持更新检查"; return; }
        Busy = true;
        Status = "正在检查更新…";
        try
        {
            var mgr = new ModManager(EffectiveDir, _http, _downloader);
            var mods = await mgr.CheckForUpdatesAsync(default);
            var updates = mods.Count(m => m.HasUpdate);
            Status = updates > 0 ? $"{updates} 个 Mod 有可用更新" : "所有 Mod 均为最新";
        }
        catch (Exception ex) { Status = $"检查失败：{ex.Message}"; }
        finally { Busy = false; }
    }

    private LoaderType LoaderFilter => DetectedLoader switch
    {
        ModLoaderKind.Fabric => LoaderType.Fabric,
        ModLoaderKind.Forge => LoaderType.Forge,
        ModLoaderKind.Quilt => LoaderType.Quilt,
        ModLoaderKind.NeoForge => LoaderType.NeoForge,
        _ => LoaderType.Any
    };
}

/// <summary>隔离模式下拉项。</summary>
public class IsolationChoice
{
    public IsolationChoice(IsolationMode mode, string text)
    {
        Mode = mode;
        Text = text;
    }

    public IsolationMode Mode { get; }
    public string Text { get; }
}

/// <summary>模组 / 资源包 / 光影分页下拉项。</summary>
public class ModTabChoice
{
    public ModTabChoice(VersionSettingsViewModel.ModTabKind kind, string text)
    {
        Kind = kind;
        Text = text;
    }

    public VersionSettingsViewModel.ModTabKind Kind { get; }
    public string Text { get; }
}

/// <summary>已安装项（Mod / 资源包 / 光影）的展示模型。</summary>
public class InstalledItemViewModel : ObservableObject
{
    public string FileName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Kind { get; set; } = "";
}

/// <summary>Modrinth 搜索命中项的展示模型。</summary>
public class ModSearchHit : ObservableObject
{
    public string ProjectId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string IconUrl { get; set; } = "";
}
