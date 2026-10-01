using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Chert.Core.Launcher;
using Chert.Core.Mvvm;
using Chert.Core.Profiles;
using Chert.App.Services;
using Chert.App.Views;

namespace Chert.App.ViewModels;

/// <summary>
/// 已安装版本列表条目。除 Id / 类型外，额外展示该版本的<b>有效工作目录</b>与<b>模组加载器</b>，
/// 让「版本隔离」在列表上就可见（对齐 MCLCS-Linux 版本列表页）。
/// </summary>
public class VersionEntry : ObservableObject
{
    private string _effectiveDir = "";

    public string Id { get; init; } = "";
    public string Type { get; init; } = "";

    /// <summary>该版本实际使用的游戏工作目录（受每版本隔离设置影响）。</summary>
    public string EffectiveDir
    {
        get => _effectiveDir;
        set
        {
            if (!SetField(ref _effectiveDir, value)) return;
            OnPropertyChanged(nameof(IsIsolated));
            OnPropertyChanged(nameof(IsolationText));
        }
    }

    /// <summary>检测到的模组加载器。</summary>
    public ModLoaderKind Loader { get; init; }

    public string LoaderText => Loader switch
    {
        ModLoaderKind.Fabric => "Fabric",
        ModLoaderKind.Forge => "Forge",
        ModLoaderKind.Quilt => "Quilt",
        ModLoaderKind.NeoForge => "NeoForge",
        _ => "原版"
    };

    /// <summary>是否处于隔离工作目录（与 .minecraft 根目录不同）。</summary>
    public bool IsIsolated =>
        !string.IsNullOrWhiteSpace(EffectiveDir)
        && !string.Equals(EffectiveDir, LauncherService.Instance.GameRoot, StringComparison.OrdinalIgnoreCase);

    public string IsolationText => IsIsolated ? "隔离" : "共享";

    /// <summary>是否存在缺失的前置项（父版本 / 客户端 jar / Mod 强制依赖）。</summary>
    public bool HasMissingPrerequisite { get; private set; }

    /// <summary>列表徽章上的摘要文案（如「缺 3 项前置」），无缺失时为空。</summary>
    public string MissingPrerequisiteText { get; private set; } = "";

    /// <summary>悬停详情：逐行列出缺失项。</summary>
    public string MissingPrerequisiteDetail { get; private set; } = "";

    /// <summary>
    /// 回填缺失前置结果。<see cref="VersionListViewModel"/> 在后台扫描完成后调用（已在 UI 线程）。
    /// </summary>
    public void SetMissingPrerequisites(IReadOnlyList<Chert.Core.Launcher.VersionPrerequisiteInfo> items)
    {
        HasMissingPrerequisite = items.Count > 0;
        MissingPrerequisiteText = items.Count == 0 ? "" : $"缺 {items.Count} 项前置";
        MissingPrerequisiteDetail = items.Count == 0
            ? ""
            : string.Join("\n", items.Select(i => "· " + i.Display));
        OnPropertyChanged(nameof(HasMissingPrerequisite));
        OnPropertyChanged(nameof(MissingPrerequisiteText));
        OnPropertyChanged(nameof(MissingPrerequisiteDetail));
    }

    /// <summary>清单 #64：该版本工作目录下的 Mod / 材质 / 光影 / 数据包 / 存档 安装情况。</summary>
    public VersionContentSnapshot Content { get; set; } = VersionContentSnapshot.Empty;

    /// <summary>列表项上的内容摘要（如「Mod 12 · 光影 3」），无内容时为空。</summary>
    public string ContentSummary => Content.Summary;

    /// <summary>是否安装了指定类别的内容（类别键与 UI 的 Tag 一致）。</summary>
    public bool HasContent(string? key) => Content.CountOf(key) > 0;

    public string DisplayName => string.IsNullOrEmpty(Type) ? Id : $"{Id} ({Type})";

    public override string ToString() => DisplayName;
}

/// <summary>
/// 已安装版本管理（对齐 MCLCS-Linux 版本列表页）：
/// 枚举 <c>versions/</c> 下含 <c>&lt;id&gt;/&lt;id&gt;.json</c> 的目录，支持刷新、一键启动与版本设置。
/// 启动统一走 <see cref="LaunchCoordinator"/>（含存档兼容检测、缺失前置安装、崩溃自动修复），
/// 每版本覆盖层（Java / 内存 / 分辨率 / 全屏 / 工作目录 / 绑定账号）由 LauncherService 自动叠加。
/// </summary>
public class VersionListViewModel : ObservableObject
{
    private ObservableCollection<VersionEntry> _versions = new();
    private VersionEntry? _selectedVersion;
    private string _statusMessage = "";
    private bool _isBusy;

    // 清单 #64：多维度分类检索（全量条目 + 检索条件）
    private List<VersionEntry> _allVersions = new();
    private ObservableCollection<VersionEntry> _filteredVersions = new();
    private string _searchText = "";
    private string _loaderFilter = "";
    private string _isolationFilter = "";
    private bool _missingPrerequisiteOnly;
    private string _contentFilter = "";

    public ObservableCollection<VersionEntry> Versions
    {
        get => _versions;
        set => SetField(ref _versions, value);
    }

    public VersionEntry? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (!SetField(ref _selectedVersion, value)) return;
            OnPropertyChanged(nameof(CanLaunch));
            if (value is not null)
            {
                // 持久化「最后选择的版本」，使快速启动下拉在重启/刷新后保持选中（bug：下拉不保持）
                try
                {
                    var p = ProfileStore.Load(LauncherService.Instance.GameRoot);
                    p.LastVersionId = value.Id;
                    ProfileStore.Save(p);
                }
                catch { /* 持久化失败不影响本次选择 */ }
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (!SetField(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanLaunch));
        }
    }

    public bool CanLaunch => !IsBusy && SelectedVersion is not null;

    public ICommand RefreshCommand { get; }
    public ICommand LaunchCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand ResetFiltersCommand { get; }

    // ===== 清单 #64：多维度分类检索 =====

    /// <summary>列表实际展示的条目（全量 <see cref="Versions"/> 经检索条件过滤后的结果）。</summary>
    public ObservableCollection<VersionEntry> FilteredVersions
    {
        get => _filteredVersions;
        set => SetField(ref _filteredVersions, value);
    }

    /// <summary>关键字检索：匹配版本 ID / 类型 / 加载器名。</summary>
    public string SearchText
    {
        get => _searchText;
        set { if (SetField(ref _searchText, value ?? "")) ApplyFilter(); }
    }

    /// <summary>模组加载器筛选：空=全部，否则为 ModLoaderKind 枚举名。</summary>
    public string LoaderFilter
    {
        get => _loaderFilter;
        set { if (SetField(ref _loaderFilter, value ?? "")) ApplyFilter(); }
    }

    /// <summary>工作目录形态：空=全部，iso=隔离，shared=共享。</summary>
    public string IsolationFilter
    {
        get => _isolationFilter;
        set { if (SetField(ref _isolationFilter, value ?? "")) ApplyFilter(); }
    }

    /// <summary>只看存在缺失前置的版本。</summary>
    public bool MissingPrerequisiteOnly
    {
        get => _missingPrerequisiteOnly;
        set { if (SetField(ref _missingPrerequisiteOnly, value)) ApplyFilter(); }
    }

    /// <summary>内容分类检索：空=全部，否则为 mods / resourcepacks / shaderpacks / datapacks / saves。</summary>
    public string ContentFilter
    {
        get => _contentFilter;
        set { if (SetField(ref _contentFilter, value ?? "")) ApplyFilter(); }
    }

    /// <summary>当前是否启用了任一检索条件（供「清空检索」按钮启用状态使用）。</summary>
    public bool HasFilter =>
        !string.IsNullOrWhiteSpace(SearchText)
        || !string.IsNullOrEmpty(LoaderFilter)
        || !string.IsNullOrEmpty(IsolationFilter)
        || MissingPrerequisiteOnly
        || !string.IsNullOrEmpty(ContentFilter);

    /// <summary>bug #10：请求以大页形式打开某版本的版本设置（由 VersionListView 订阅并导航）。</summary>
    public event Action<VersionEntry>? SettingsRequested;

    public VersionListViewModel()
    {
        RefreshCommand = new RelayCommand(_ => Refresh());
        LaunchCommand = new AsyncRelayCommand(_ => LaunchAsync(), _ => CanLaunch);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        ResetFiltersCommand = new RelayCommand(_ => ResetFilters());
        Refresh();
    }

    public void Refresh()
    {
        var gameRoot = LauncherService.Instance.GameRoot;
        var list = new ObservableCollection<VersionEntry>();

        foreach (var (id, type) in LauncherService.Instance.ListInstalledVersions())
        {
            var vp = VersionProfileStore.Load(gameRoot, id);
            list.Add(new VersionEntry
            {
                Id = id,
                Type = type,
                Loader = VersionProfileStore.DetectLoader(gameRoot, id),
                EffectiveDir = VersionProfileStore.HasProfile(gameRoot, id)
                    ? VersionProfileStore.EffectiveGameDir(gameRoot, id, vp)
                    : VersionIsolation.GameDirFor(gameRoot, id)
            });
        }

        _allVersions = list.ToList();
        // 清单 #64：统计各版本工作目录里的内容安装情况，供「安装了 X 的版本」分类检索
        foreach (var entry in _allVersions)
            entry.Content = VersionContentScanner.Scan(entry.EffectiveDir);

        Versions = list;
        ApplyFilter();

        // 恢复上次选中的版本（持久化在 profile.LastVersionId），使快速启动下拉在刷新后实时回显选中态
        var lastId = "";
        try { lastId = ProfileStore.Load(LauncherService.Instance.GameRoot).LastVersionId ?? ""; } catch { }
        SelectedVersion = Versions.FirstOrDefault(v => v.Id == lastId) ?? Versions.FirstOrDefault();

        // 缺失前置扫描要解析每个 Mod 的 jar 元数据，较慢；放后台逐个回填，
        // 列表先出内容、徽章随后补上，避免刷新卡顿。
        _ = ScanPrerequisitesAsync(list.ToList(), gameRoot);
    }

    /// <summary>清单 #64：按当前检索条件重算 <see cref="FilteredVersions"/>。</summary>
    private void ApplyFilter()
    {
        IEnumerable<VersionEntry> query = _allVersions;

        var keyword = (_searchText ?? "").Trim();
        if (keyword.Length > 0)
            query = query.Where(v =>
                v.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || v.Type.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || v.LoaderText.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(_loaderFilter))
            query = query.Where(v => string.Equals(v.Loader.ToString(), _loaderFilter,
                StringComparison.OrdinalIgnoreCase));

        if (_isolationFilter == "iso") query = query.Where(v => v.IsIsolated);
        else if (_isolationFilter == "shared") query = query.Where(v => !v.IsIsolated);

        if (_missingPrerequisiteOnly) query = query.Where(v => v.HasMissingPrerequisite);

        if (!string.IsNullOrEmpty(_contentFilter))
            query = query.Where(v => v.HasContent(_contentFilter));

        FilteredVersions = new ObservableCollection<VersionEntry>(query);
        OnPropertyChanged(nameof(HasFilter));

        StatusMessage = _allVersions.Count == 0
            ? "暂无已安装版本，请前往「安装新版本」"
            : HasFilter
                ? $"共 {_allVersions.Count} 个版本，筛选出 {FilteredVersions.Count} 个"
                : $"共发现 {_allVersions.Count} 个版本";
    }

    /// <summary>清单 #64：清空全部检索条件。</summary>
    private void ResetFilters()
    {
        _searchText = "";
        _loaderFilter = "";
        _isolationFilter = "";
        _missingPrerequisiteOnly = false;
        _contentFilter = "";
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(LoaderFilter));
        OnPropertyChanged(nameof(IsolationFilter));
        OnPropertyChanged(nameof(MissingPrerequisiteOnly));
        OnPropertyChanged(nameof(ContentFilter));
        ApplyFilter();
    }

    /// <summary>后台逐个扫描版本的缺失前置，并切回 UI 线程回填到条目。</summary>
    private async Task ScanPrerequisitesAsync(List<VersionEntry> entries, string gameRoot)
    {
        foreach (var entry in entries)
        {
            try
            {
                var missing = await Task.Run(() => VersionPrerequisiteScanner.Scan(
                    gameRoot, entry.Id, entry.EffectiveDir));
                if (missing.Count == 0) continue;

                await System.Windows.Application.Current.Dispatcher
                    .InvokeAsync(() =>
                    {
                        entry.SetMissingPrerequisites(missing);
                        // 缺失前置可作为检索条件，回填后立即重算筛选结果
                        ApplyFilter();
                    });
            }
            catch
            {
                // 单个版本扫描失败不影响其余版本
            }
        }
    }

    private async Task LaunchAsync()
    {
        if (SelectedVersion is null)
        {
            StatusMessage = "请先选择一个版本";
            return;
        }

        IsBusy = true;
        try
        {
            // 统一启动流程（含存档兼容检测、缺失前置安装、崩溃自动修复）由 LaunchCoordinator 负责
            await LaunchCoordinator.LaunchAsync(SelectedVersion.Id, s => StatusMessage = s);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenSettings(object? parameter)
    {
        var entry = parameter as VersionEntry ?? SelectedVersion;
        if (entry is null)
        {
            StatusMessage = "请先选择一个版本";
            return;
        }

        // bug #10：不再以模态窗口打开，改为大页导航（由 VersionListView 订阅 SettingsRequested）
        SettingsRequested?.Invoke(entry);
    }
}
