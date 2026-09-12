using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Chert.App.Services;
using Chert.Core.Download;
using Chert.Core.Installers;
using Chert.Core.Models;
using Chert.Core.Mvvm;
using Chert.Core.Utils;
namespace Chert.App.ViewModels;

/// <summary>整合包在线来源的绑定包装（ComboBox 用）。</summary>
public class ModpackSourceEntry
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public bool IsAvailable { get; init; }
    public string? UnavailableReason { get; init; }

    public override string ToString() => DisplayName;
}

/// <summary>
/// bug #14：mod / 光影 / 资源包的项目详情绑定模型。
/// 此前这三类点详情会直接跳转 Modrinth 网页，既没有页内详情，也没有「返回」的余地；
/// 现在改为在启动器内爬取版本列表并支持选择版本后安装。
/// </summary>
public class ModrinthProjectDetail
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string FallbackToken { get; init; } = "mod";
    public string SubTab { get; init; } = "mod";
    public string ProjectUrl { get; init; } = "";
    public List<ProjectVersionChoice> Versions { get; set; } = new();

    public bool HasVersions => Versions.Count > 0;
    public int VersionCount => Versions.Count;

    /// <summary>版本聚合出的加载器摘要。</summary>
    public string Loaders =>
        Versions.Count == 0 ? "-" : string.Join(", ",
            Versions.SelectMany(v => v.LoaderSummary.Split(',', StringSplitOptions.TrimEntries))
                    .Where(s => s.Length > 0 && s != "-")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(4));
}

/// <summary>
/// 下载页（规格 2.2）ViewModel：按全局侧边栏副标签（mod / shader / resourcepack / modpack / map）切换内容，
/// 顶部版本 + 加载器筛选（地图为分类 / 版本 / 排序 / 分页），卡片网格（外联封面由 ExternalIcon 渲染），
/// 底部下载队列（进度 / 暂停 / 取消）。Mod 类走 Modrinth，地图走像素茶艺（PixelMap），
/// 整合包在线浏览走 <see cref="IModpackSource"/>（当前仅 Modrinth 常驻可用）。
/// </summary>
public class DownloadPageViewModel : ObservableObject
{
    /// <summary>Minecraft 版本分组（按大版本折叠，bug #21：避免一次性渲染全部版本导致卡顿/无响应）。</summary>
    public class VersionGroup
    {
        public string Header { get; init; } = "";
        public ObservableCollection<DownloadCardItem> Items { get; } = new();
        public int Count => Items.Count;
    }

    /// <summary>
    /// 当前下载页 VM 实例。供标题栏全局下载进度弹窗访问同一份队列
    /// （bug #14：下载队列合并至全局搜索栏右侧的下载进度弹窗）。
    /// </summary>
    public static DownloadPageViewModel? Current { get; private set; }

    private string _currentSubTab = "mod";
    private bool _isMap;
    private string _query = "";
    private string _selectedGameVersion = "";
    private string _selectedLoader = "Any";
    private string _statusMessage = "";
    private bool _isBusy;
    private bool _isSearching;
    private int _searchSeq;
    private DownloadCardItem? _selectedCard;

    // 地图专属
    private string _selectedCategory = "";
    private string _selectedMapVersion = "";
    private MapSort _mapSort = MapSort.Published;
    private int _mapPage = 1;
    private int _mapTotalPages = 1;
    private bool _mapFacetsLoaded;

    // bug #16：通用分页（mod / 光影 / 资源包 / 整合包 / 地图共用一套页码；Minecraft 页用分组折叠，不分页）
    private int _page = 1;
    private int _totalPages = 1;

    /// <summary>在途搜索的取消源：切换副页/重新搜索时取消旧请求，避免慢请求后到覆盖新页内容（bug #16）。</summary>
    private CancellationTokenSource? _searchCts;

    // 地图详情弹窗
    private PixelMapDetail? _currentMapDetail;
    private bool _isDetailOpen;
    private string _detailHint = "";

    // 整合包在线浏览（规格 2.2 → 整合包：在线浏览 Modrinth，一键安装）
    private bool _isModpack;
    private ObservableCollection<ModpackSourceEntry> _modpackSources = new();
    private string _selectedModpackSource = "modrinth";
    private ModpackDetail? _currentModpackDetail;
    private bool _isModpackDetailOpen;
    private ModpackVersion? _selectedModpackVersion;
    private bool _installIsolated = true;
    private string _currentModpackSourceId = "modrinth";
    private string _modpackDetailHint = "";

    // bug #14：Modrinth 项目（mod / 光影 / 资源包）页内详情
    private ModrinthProjectDetail? _currentProjectDetail;
    private bool _isProjectDetailOpen;
    private ProjectVersionChoice? _selectedProjectVersion;
    private string _projectDetailHint = "";
    private string _projectTranslated = "";

    // Minecraft 版本下载（规格新增）
    private bool _isMinecraft;
    // 注意：本命名空间下另有一个用于版本列表页展示的 VersionEntry（仅 Id/Type），
    // 会屏蔽 using 别名，故此处用完全限定名指向 Core 的清单实体（带 Url / ReleaseTime）。
    private List<Chert.Core.Models.VersionEntry>? _versionEntries;
    private string _selectedVersionType = "all";
    private DownloadCardItem? _currentInstallVersion;
    private bool _isInstallOpen;
    private string _selectedInstallLoader = "none";
    private bool _isInstalling;
    private string _installStatus = "";

    public ObservableCollection<DownloadCardItem> Cards { get; } = new();
    public ObservableCollection<DownloadQueueItem> Queue { get; } = new();

    /// <summary>Minecraft 版本分组（折叠卡片用，bug #21）。</summary>
    public ObservableCollection<VersionGroup> VersionGroups { get; } = new();

    public ObservableCollection<string> Loaders { get; } =
        new() { "Any", "Fabric", "Forge", "Quilt", "NeoForge" };

    public ObservableCollection<GameVersionItem> GameVersions { get; } = new() { new GameVersionItem() };
    public ObservableCollection<PixelMapCategory> MapCategories { get; } = new();
    public ObservableCollection<string> MapVersions { get; } = new() { "" };

    public string CurrentSubTab
    {
        get => _currentSubTab;
        set => SetField(ref _currentSubTab, value);
    }

    /// <summary>当前是否为 Mod 副标签（仅 Mod 需要加载器筛选，资源包/光影不需要）。</summary>
    public bool IsMod => _currentSubTab == "mod";

    /// <summary>当前是否为地图副标签（驱动筛选行显隐）。</summary>
    public bool IsMap
    {
        get => _isMap;
        private set
        {
            if (SetField(ref _isMap, value))
                RaiseFilterVisibilityChanged();
        }
    }

    /// <summary>当前是否为 Minecraft 版本下载副标签。</summary>
    public bool IsMinecraft
    {
        get => _isMinecraft;
        private set
        {
            if (SetField(ref _isMinecraft, value))
                RaiseFilterVisibilityChanged();
            // 分页条显隐依赖当前是否为 Minecraft 页（bug #16）
            OnPropertyChanged(nameof(HasPaging));
        }
    }

    /// <summary>当前是否为整合包在线浏览副标签。</summary>
    public bool IsModpack
    {
        get => _isModpack;
        private set
        {
            if (SetField(ref _isModpack, value))
                RaiseFilterVisibilityChanged();
        }
    }

    /// <summary>
    /// 统一通知全部派生的筛选行可见性属性。
    /// 修复 bug #9：此前 IsMap / IsMinecraft / IsModpack 变化时未通知 <see cref="ModrinthFilterVisible"/>，
    /// 导致切换副标签后旧筛选行不隐藏，与新筛选行同处 Grid.Row=1 相互重叠（加载器下拉框错位）。
    /// </summary>
    private void RaiseFilterVisibilityChanged()
    {
        OnPropertyChanged(nameof(ShowModFilters));
        OnPropertyChanged(nameof(ModpackSourceVisible));
        OnPropertyChanged(nameof(ModrinthFilterVisible));
    }

    /// <summary>整合包来源选择行是否可见（仅整合包子标签）。</summary>
    public bool ModpackSourceVisible => IsModpack;

    /// <summary>Modrinth 风格筛选行（版本 + 加载器）是否可见：非地图、非 Minecraft、非整合包。</summary>
    public bool ModrinthFilterVisible => !IsMap && !IsMinecraft && !IsModpack;

    /// <summary>是否显示 Modrinth 风格的 版本+加载器 筛选行（非地图且非 Minecraft 时）。</summary>
    public bool ShowModFilters => !IsMap && !IsMinecraft;

    /// <summary>Minecraft 版本类型筛选：all / release / snapshot / old。</summary>
    public string SelectedVersionType
    {
        get => _selectedVersionType;
        set
        {
            if (SetField(ref _selectedVersionType, value) && IsMinecraft)
                ApplyVersionFilter();
        }
    }

    /// <summary>当前在弹窗中待安装的 Minecraft 版本卡片。</summary>
    public DownloadCardItem? CurrentInstallVersion
    {
        get => _currentInstallVersion;
        set => SetField(ref _currentInstallVersion, value);
    }

    /// <summary>安装弹窗是否打开。</summary>
    public bool IsInstallOpen
    {
        get => _isInstallOpen;
        set => SetField(ref _isInstallOpen, value);
    }

    /// <summary>选中的加载器：none / forge / fabric / neoforge / quilt。</summary>
    public string SelectedInstallLoader
    {
        get => _selectedInstallLoader;
        set => SetField(ref _selectedInstallLoader, value);
    }

    /// <summary>安装进行中（弹窗内显示进度）。</summary>
    public bool IsInstalling
    {
        get => _isInstalling;
        set => SetField(ref _isInstalling, value);
    }

    /// <summary>安装状态文案（弹窗内显示）。</summary>
    public string InstallStatus
    {
        get => _installStatus;
        set => SetField(ref _installStatus, value);
    }

    public string Query
    {
        get => _query;
        set
        {
            if (SetField(ref _query, value) && IsMinecraft)
                ApplyVersionFilter();
        }
    }

    public string SelectedGameVersion
    {
        get => _selectedGameVersion;
        set => SetField(ref _selectedGameVersion, value);
    }

    public string SelectedLoader
    {
        get => _selectedLoader;
        set => SetField(ref _selectedLoader, value);
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set => SetField(ref _selectedCategory, value);
    }

    public string SelectedMapVersion
    {
        get => _selectedMapVersion;
        set => SetField(ref _selectedMapVersion, value);
    }

    public MapSort MapSort
    {
        get => _mapSort;
        set => SetField(ref _mapSort, value);
    }

    /// <summary>地图排序（字符串形式，便于 ComboBox 双向绑定：published / views）。</summary>
    public string MapSortKey
    {
        get => _mapSort == MapSort.Views ? "views" : "published";
        set => MapSort = value == "views" ? MapSort.Views : MapSort.Published;
    }

    /// <summary>地图页码。与通用 <see cref="Page"/> 同源（bug #16），保留属性名以兼容既有绑定。</summary>
    public int MapPage
    {
        get => Page;
        set => Page = value;
    }

    /// <summary>地图总页数。与通用 <see cref="TotalPages"/> 同源（bug #16）。</summary>
    public int MapTotalPages
    {
        get => TotalPages;
        set => TotalPages = value;
    }

    /// <summary>当前页码（1 起）。mod / 光影 / 资源包 / 整合包 / 地图共用。</summary>
    public int Page
    {
        get => _page;
        set
        {
            if (SetField(ref _page, value))
            {
                OnPropertyChanged(nameof(CanPrevPage));
                OnPropertyChanged(nameof(CanNextPage));
            }
        }
    }

    /// <summary>总页数。Minecraft 页使用分组折叠展示，恒为 1。</summary>
    public int TotalPages
    {
        get => _totalPages;
        set
        {
            if (SetField(ref _totalPages, value))
            {
                OnPropertyChanged(nameof(HasPaging));
                OnPropertyChanged(nameof(CanPrevPage));
                OnPropertyChanged(nameof(CanNextPage));
            }
        }
    }

    /// <summary>是否展示分页条：Minecraft 页不分页，其余页总页数大于 1 时展示。</summary>
    public bool HasPaging => !IsMinecraft && _totalPages > 1;

    public bool CanPrevPage => _page > 1;

    public bool CanNextPage => _page < _totalPages;

    public DownloadCardItem? SelectedCard
    {
        get => _selectedCard;
        set => SetField(ref _selectedCard, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    /// <summary>是否正在执行搜索（驱动卡片区加载遮罩，与 IsBusy 区分以免影响下载队列等其他忙碌态）。</summary>
    public bool IsSearching
    {
        get => _isSearching;
        set => SetField(ref _isSearching, value);
    }

    public PixelMapDetail? CurrentMapDetail
    {
        get => _currentMapDetail;
        set
        {
            if (!SetField(ref _currentMapDetail, value)) return;
            OnPropertyChanged(nameof(DetailCanDownload));
            OnPropertyChanged(nameof(DetailHasExtra));
        }
    }

    /// <summary>详情窗：地图本体可直接下载（作者允许 + 有直链）。</summary>
    public bool DetailCanDownload => CurrentMapDetail?.CanDownload == true;

    /// <summary>详情窗：存在附加资源（资源包 / 光影），决定"附加资源"按钮显隐。</summary>
    public bool DetailHasExtra => CurrentMapDetail?.HasAdditionalResources == true;

    /// <summary>详情窗底部提示（下载 / 安装结果、不可下载原因）。</summary>
    public string DetailHint
    {
        get => _detailHint;
        set => SetField(ref _detailHint, value);
    }

    public bool IsDetailOpen
    {
        get => _isDetailOpen;
        set => SetField(ref _isDetailOpen, value);
    }

    // ---- 整合包在线浏览 ----

    public ObservableCollection<ModpackSourceEntry> ModpackSources
    {
        get => _modpackSources;
        set => SetField(ref _modpackSources, value);
    }

    public string SelectedModpackSource
    {
        get => _selectedModpackSource;
        set
        {
            if (SetField(ref _selectedModpackSource, value))
                _ = SearchAsync();
        }
    }

    public ModpackDetail? CurrentModpackDetail
    {
        get => _currentModpackDetail;
        set
        {
            if (!SetField(ref _currentModpackDetail, value)) return;
            OnPropertyChanged(nameof(ModpackDetailCanInstall));
            OnPropertyChanged(nameof(ModpackDetailSourceNote));
            OnPropertyChanged(nameof(ModpackDetailLoaders));
            OnPropertyChanged(nameof(ModpackDetailLatestVersion));
        }
    }

    public bool IsModpackDetailOpen
    {
        get => _isModpackDetailOpen;
        set => SetField(ref _isModpackDetailOpen, value);
    }

    public ModpackVersion? SelectedModpackVersion
    {
        get => _selectedModpackVersion;
        set
        {
            if (SetField(ref _selectedModpackVersion, value))
            {
                OnPropertyChanged(nameof(ModpackDetailCanInstall));
                _modpackDetailHint = "";
                OnPropertyChanged(nameof(ModpackDetailHint));
            }
        }
    }

    /// <summary>隔离安装开关（默认开启，符合独立版本隔离目录决策）。</summary>
    public bool InstallIsolated
    {
        get => _installIsolated;
        set => SetField(ref _installIsolated, value);
    }

    /// <summary>当前打开详情的整合包所属来源 Id（驱动安装/打开页面行为）。</summary>
    public string CurrentModpackSourceId
    {
        get => _currentModpackSourceId;
        set => SetField(ref _currentModpackSourceId, value);
    }

    /// <summary>详情窗：所选版本是否有可直链下载的文件。</summary>
    public bool ModpackDetailCanInstall => !string.IsNullOrEmpty(SelectedModpackVersion?.FileUrl);

    /// <summary>详情窗：来源相关的提示（当前仅 Modrinth，预留给未来扩展）。</summary>
    public string ModpackDetailSourceNote => "";

    /// <summary>详情窗：从版本列表聚合的加载器摘要（首字母大写、去重）。</summary>
    public string ModpackDetailLoaders
    {
        get
        {
            if (CurrentModpackDetail is null) return "";
            var loaders = CurrentModpackDetail.Versions
                .Select(v => v.Loader)
                .Where(l => !string.IsNullOrEmpty(l))
                .Select(l => char.ToUpperInvariant(l![0]) + l[1..])
                .Distinct()
                .ToList();
            return string.Join(" / ", loaders);
        }
    }

    /// <summary>详情窗：最新支持的游戏版本（版本列表首项）。</summary>
    public string ModpackDetailLatestVersion => CurrentModpackDetail?.Versions.FirstOrDefault()?.GameVersion ?? "";

    public string ModpackDetailHint
    {
        get => _modpackDetailHint;
        set => SetField(ref _modpackDetailHint, value);
    }

    public ICommand SetSubTabCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand EnqueueCommand { get; }
    public ICommand StartQueueCommand { get; }
    public ICommand PauseItemCommand { get; }
    public ICommand CancelItemCommand { get; }
    public ICommand OpenDetailCommand { get; }
    public ICommand ChangeMapPageCommand { get; }

    /// <summary>通用翻页命令（bug #16：mod / 光影 / 资源包 / 整合包 / 地图共用）。</summary>
    public ICommand ChangePageCommand { get; }
    public ICommand SetModpackSourceCommand { get; }
    public ICommand OpenModpackDetailCommand { get; }
    public ICommand InstallModpackCommand { get; }
    public ICommand CloseModpackDetailCommand { get; }
    public ICommand OpenModpackPageCommand { get; }
    public ICommand DownloadDetailCommand { get; }

    /// <summary>下载并分发地图附加资源（资源包 → resourcepacks，光影 → shaderpacks）。</summary>
    public ICommand DownloadExtraCommand { get; }

    /// <summary>在浏览器打开地图站详情页（作者未授权直接下载时的出路）。</summary>
    public ICommand OpenMapPageCommand { get; }

    public ICommand CloseDetailCommand { get; }

    /// <summary>关闭项目详情弹窗（bug #14：mod / 光影 / 资源包详情返回）。</summary>
    public ICommand CloseProjectDetailCommand { get; }

    /// <summary>把详情页所选版本加入下载队列（bug #14）。</summary>
    public ICommand InstallProjectVersionCommand { get; }

    /// <summary>AI 翻译项目描述（未启用 AI 助手时按钮置灰，bug #14）。</summary>
    public ICommand TranslateDetailCommand { get; }

    /// <summary>在浏览器打开项目页（详情页内的外部入口）。</summary>
    public ICommand OpenProjectPageCommand { get; }

    public ICommand EnqueueInstallCommand { get; }
    public ICommand CloseInstallCommand { get; }

    public DownloadPageViewModel()
    {
        SetSubTabCommand = new RelayCommand(p => SetSubTab(p as string));
        SearchCommand = new AsyncRelayCommand(_ => SearchAsync(), _ => !IsBusy);
        EnqueueCommand = new RelayCommand(p => EnqueueSelected(p as DownloadCardItem));
        StartQueueCommand = new AsyncRelayCommand(_ => StartQueueAsync(), _ => !IsBusy);
        PauseItemCommand = new RelayCommand(p => PauseItem(p as DownloadQueueItem));
        CancelItemCommand = new RelayCommand(p => CancelItem(p as DownloadQueueItem));
        OpenDetailCommand = new RelayCommand(p => OpenDetail(p as DownloadCardItem));
        ChangeMapPageCommand = new RelayCommand(p => ChangePage(p as string));
        ChangePageCommand = new RelayCommand(p => ChangePage(p as string));
        SetModpackSourceCommand = new RelayCommand(p => SetModpackSource(p as string));
        OpenModpackDetailCommand = new RelayCommand(p => _ = OpenModpackDetailAsync(p as DownloadCardItem));
        InstallModpackCommand = new AsyncRelayCommand(_ => InstallModpackAsync(), _ => ModpackDetailCanInstall && !IsBusy);
        CloseModpackDetailCommand = new RelayCommand(_ => IsModpackDetailOpen = false);
        OpenModpackPageCommand = new RelayCommand(_ => OpenModpackPage());
        DownloadDetailCommand = new AsyncRelayCommand(_ => DownloadDetailAsync(), _ => DetailCanDownload && !IsBusy);
        DownloadExtraCommand = new AsyncRelayCommand(_ => DownloadExtraAsync(), _ => DetailHasExtra && !IsBusy);
        OpenMapPageCommand = new RelayCommand(_ => OpenMapPage());
        CloseDetailCommand = new RelayCommand(_ => IsDetailOpen = false);
        CloseProjectDetailCommand = new RelayCommand(_ => IsProjectDetailOpen = false);
        InstallProjectVersionCommand = new AsyncRelayCommand(
            _ => InstallProjectVersionAsync(), _ => ProjectDetailCanInstall && !IsBusy);
        // 未启用 AI 助手时置灰（bug #14）
        TranslateDetailCommand = new AsyncRelayCommand(
            _ => TranslateDetailAsync(), _ => AiEnabled && !IsBusy);
        OpenProjectPageCommand = new RelayCommand(_ => OpenProjectPage());
        // 「加入队列」按钮在版本已选中的安装弹窗内，直接始终可用，不再门控置灰（修复：下载队列按钮持续置灰）。
        EnqueueInstallCommand = new RelayCommand(_ => EnqueueVersionInstall());
        CloseInstallCommand = new RelayCommand(_ => IsInstallOpen = false);

        Current = this;
        Queue.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(QueueCount));
            OnPropertyChanged(nameof(HasQueue));
        };

        _ = LoadGameVersionsAsync();
    }

    // ---- Modrinth 项目详情（bug #14：mod / 光影 / 资源包）----

    public ModrinthProjectDetail? CurrentProjectDetail
    {
        get => _currentProjectDetail;
        set
        {
            if (!SetField(ref _currentProjectDetail, value)) return;
            OnPropertyChanged(nameof(ProjectDetailCanInstall));
            OnPropertyChanged(nameof(ProjectDetailHasVersions));
            OnPropertyChanged(nameof(ProjectDetailLoaders));
        }
    }

    public bool IsProjectDetailOpen
    {
        get => _isProjectDetailOpen;
        set => SetField(ref _isProjectDetailOpen, value);
    }

    public ProjectVersionChoice? SelectedProjectVersion
    {
        get => _selectedProjectVersion;
        set
        {
            if (SetField(ref _selectedProjectVersion, value))
            {
                _projectDetailHint = "";
                OnPropertyChanged(nameof(ProjectDetailHint));
                OnPropertyChanged(nameof(ProjectDetailCanInstall));
            }
        }
    }

    public string ProjectDetailHint
    {
        get => _projectDetailHint;
        set => SetField(ref _projectDetailHint, value);
    }

    /// <summary>AI 翻译结果（详情页展示）。</summary>
    public string ProjectTranslated
    {
        get => _projectTranslated;
        set { if (SetField(ref _projectTranslated, value)) OnPropertyChanged(nameof(ProjectHasTranslation)); }
    }

    public bool ProjectHasTranslation => _projectTranslated.Length > 0;

    public bool ProjectDetailCanInstall => !string.IsNullOrEmpty(SelectedProjectVersion?.FileUrl);
    public bool ProjectDetailHasVersions => CurrentProjectDetail?.HasVersions ?? false;
    public string ProjectDetailLoaders => CurrentProjectDetail?.Loaders ?? "-";

    /// <summary>AI 助手是否启用：未启用时详情页「AI 翻译」按钮置灰（bug #14）。</summary>
    public bool AiEnabled => Chert.Core.Ai.Assistant.Config.Enabled;

    /// <summary>队列项数（标题栏下载弹窗角标用）。</summary>
    public int QueueCount => Queue.Count;

    /// <summary>队列是否非空（控制标题栏下载按钮角标显隐）。</summary>
    public bool HasQueue => Queue.Count > 0;

    /// <summary>切换副标签（由 MainWindow 侧边栏路由调用）。</summary>
    public void SetSubTab(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) id = "minecraft";
        if (id is not ("minecraft" or "mod" or "shader" or "resourcepack" or "modpack" or "map")) id = "minecraft";

        CurrentSubTab = id;
        OnPropertyChanged(nameof(IsMod));
        IsMap = id == "map";
        IsMinecraft = id == "minecraft";
        IsModpack = id == "modpack";

        if (id == "map" && !_mapFacetsLoaded)
            _ = LoadMapFacetsAsync();

        if (id == "modpack")
            RefreshModpackSources();

        _ = SearchAsync();
    }

    /// <summary>刷新整合包来源列表（当前仅 Modrinth 常驻）。</summary>
    private void RefreshModpackSources()
    {
        var list = LauncherService.Instance.ModpackSources
            .Select(s => new ModpackSourceEntry
            {
                Id = s.Id,
                DisplayName = s.DisplayName,
                IsAvailable = s.IsAvailable,
                UnavailableReason = s.UnavailableReason
            })
            .ToList();

        ModpackSources = new ObservableCollection<ModpackSourceEntry>(list);

        // 选中源若不可用（如刚清空 Key），回退到第一个可用源
        if (ModpackSources.FirstOrDefault(s => s.Id == _selectedModpackSource && s.IsAvailable) is null)
        {
            var first = ModpackSources.FirstOrDefault(s => s.IsAvailable) ?? ModpackSources.FirstOrDefault();
            _selectedModpackSource = first?.Id ?? "modrinth";
            OnPropertyChanged(nameof(SelectedModpackSource));
        }
    }

    /// <summary>切换整合包来源（来自 ComboBox 选择或源不可用回退）。</summary>
    private void SetModpackSource(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        if (ModpackSources.FirstOrDefault(s => s.Id == id && s.IsAvailable) is null) return;
        SelectedModpackSource = id!;
    }

    private async Task LoadGameVersionsAsync()
    {
        // 构造函数以 fire-and-forget 方式调用，必须自行吞掉异常，
        // 否则离线 / 镜像不可达时会产生未观测任务异常（bug #6：进入下载页崩溃）。
        try
        {
            GameVersions.Clear();
            // 「全部版本」（空 Id = 不过滤），始终置顶
            GameVersions.Add(new GameVersionItem { Id = "", IsInstalled = false });

            // 已安装版本（来自 versions/ 目录）置顶并标注「已装」
            var installed = LauncherService.Instance.ListInstalledVersions()
                .Select(t => t.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet();

            foreach (var id in installed)
                GameVersions.Add(new GameVersionItem { Id = id, IsInstalled = true });

            // 其余原版版本列于其后
            var versions = await LauncherService.Instance.GetVanillaVersionsAsync();
            foreach (var v in versions)
                if (!installed.Contains(v))
                    GameVersions.Add(new GameVersionItem { Id = v, IsInstalled = false });

            // 下载页版本筛选下拉默认保持「全部版本」(SelectedGameVersion 留空)，
            // 不再预选最新原版版本——否则用户每次进入下载页都被锁在某个具体版本，须手动改回。
            if (!IsMap && !IsMinecraft)
                _ = SearchAsync();
        }
        catch
        {
            if (GameVersions.Count == 0) GameVersions.Add(new GameVersionItem());
        }
    }

    private async Task LoadMapFacetsAsync()
    {
        try
        {
            var client = LauncherService.Instance.Pixelmap;
            var cats = await client.GetCategoriesAsync();
            MapCategories.Clear();
            MapCategories.Add(new PixelMapCategory { Id = "", Name = "全部分类" });
            foreach (var c in cats) MapCategories.Add(c);

            var vers = await client.GetVersionsAsync();
            MapVersions.Clear();
            MapVersions.Add("");
            foreach (var v in vers) MapVersions.Add(v);

            // 地图版本筛选下拉默认保持「全部版本」(SelectedMapVersion 留空)，不预选具体版本。

            _mapFacetsLoaded = true;
        }
        catch
        {
            // 地图站不可用时保留空列表
        }
    }

    /// <summary>搜索进行中的状态栏提示（按副标签区分，避免 Modrinth 慢响应时卡片区空白被误判为无结果）。</summary>
    private string LoadingHint() => CurrentSubTab switch
    {
        "mod" => "正在加载 Mod…",
        "shader" => "正在加载光影…",
        "resourcepack" => "正在加载资源包…",
        "modpack" => "正在加载整合包…",
        "map" => "正在加载地图…",
        _ => "正在加载版本列表…"
    };

    /// <summary>执行搜索。<paramref name="resetPage"/> 为 false 时保留当前页码（翻页场景）。</summary>
    private async Task SearchAsync(bool resetPage = true)
    {
        // bug #16：切换副页 / 重新搜索时取消仍在途的旧请求。
        // 否则慢请求后到会把上一个副页的结果写进当前页（表现为"显示的还是第一次副页的内容"）。
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var token = cts.Token;
        var tabAtStart = CurrentSubTab;

        IsBusy = true;
        var mySeq = ++_searchSeq;
        IsSearching = true;
        StatusMessage = LoadingHint();
        try
        {
            Cards.Clear();
            if (resetPage) Page = 1;

            try
            {
                if (IsMinecraft) { TotalPages = 1; await LoadVersionsAsync(); }
                else if (IsMap) await SearchMapsAsync(resetPage, token);
                else if (IsModpack) await SearchModpackAsync(token);
                else await SearchModrinthAsync(token);
            }
            catch (OperationCanceledException)
            {
                return; // 已被新搜索取代：结果丢弃，不写 UI
            }

            // bug #16：加载期间用户已切到其他副页 → 丢弃本次结果
            if (token.IsCancellationRequested || !string.Equals(tabAtStart, CurrentSubTab, StringComparison.Ordinal))
                return;

            StatusMessage = Cards.Count > 0
                ? TotalPages > 1
                    ? $"找到 {Cards.Count} 个结果（第 {Page} / {TotalPages} 页）"
                    : $"找到 {Cards.Count} 个结果"
                : "未找到结果";
        }
        catch (Exception ex)
        {
            StatusMessage = $"搜索失败：{ex.Message}";
        }
        finally
        {
            if (_searchCts == cts)
            {
                _searchCts = null;
                cts.Dispose();
            }
            if (mySeq == _searchSeq) IsSearching = false;
            IsBusy = false;
            OnPropertyChanged(nameof(HasPaging));
        }
    }

    /// <summary>通用翻页（bug #16）：next / prev，越界则无操作。</summary>
    private void ChangePage(string? dir)
    {
        if (dir == "next" && CanNextPage) Page++;
        else if (dir == "prev" && CanPrevPage) Page--;
        else return;

        OnPropertyChanged(nameof(Page));
        _ = SearchAsync(resetPage: false);
    }

    /// <summary>关键词是否含中日韩字符：Modrinth 对中文分词支持差，需要本地二次过滤兜底（bug #17）。</summary>
    private static bool HasCjk(string text)
    {
        foreach (var ch in text)
            if (ch >= '\u3400' && ch <= '\u9FFF') return true;
        return false;
    }

    /// <summary>条目字段是否包含关键词（序号无关比较，中文可用）。</summary>
    private static bool HitContains(ModrinthHit h, string kw) =>
        (h.Title?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false)
        || (h.Slug?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false)
        || (h.Description?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false);

    // ---- Minecraft 版本下载 ----

    private async Task LoadVersionsAsync()
    {
        if (_versionEntries is null)
        {
            try { _versionEntries = await LauncherService.Instance.GetVanillaVersionsDetailedAsync(); }
            catch { _versionEntries = new List<Chert.Core.Models.VersionEntry>(); }
        }
        ApplyVersionFilter();
    }

    /// <summary>
    /// 按搜索词与版本类型本地筛选（无网络），按大版本分组填充折叠卡片（bug #21：
    /// 此前一次性渲染全部版本导致卡顿/无响应，现改为分组 Expander，按需展开）。
    /// </summary>
    private void ApplyVersionFilter()
    {
        VersionGroups.Clear();
        if (_versionEntries is null) return;
        var q = (Query ?? "").Trim().ToLowerInvariant();
        var type = SelectedVersionType;

        // 先按大版本（如 1.20 / 1.19）分组
        var groups = new Dictionary<string, List<Chert.Core.Models.VersionEntry>>();
        foreach (var v in _versionEntries)
        {
            var bucket = v.Type switch
            {
                "release" => "release",
                "snapshot" => "snapshot",
                _ => "old"
            };
            if (type != "all" && bucket != type) continue;
            // bug #17：显式序号无关比较，避免文化敏感 Contains 在部分环境下的中文匹配异常
            if (q.Length > 0 && v.Id.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;

            var key = GroupKey(v.Id, bucket);
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<Chert.Core.Models.VersionEntry>();
                groups[key] = list;
            }
            list.Add(v);
        }

        // 排序：快照最前，其次按大版本号降序，旧版/其他在后
        foreach (var key in groups.Keys.OrderBy(GroupOrder).ThenByDescending(MajorMinor))
        {
            var g = new VersionGroup { Header = $"{key}（{groups[key].Count}）" };
            foreach (var v in groups[key].OrderByDescending(x => x.Id, StringComparer.OrdinalIgnoreCase))
            {
                g.Items.Add(new DownloadCardItem
                {
                    Id = v.Id,
                    Title = v.Id,
                    Author = "Mojang",
                    Summary = key == "快照 & 预览" ? "快照" : (key is "旧版本" or "其他") ? "旧版" : "正式版",
                    IconUrl = null,
                    FallbackToken = "download",
                    Source = "Minecraft",
                    SubTab = "minecraft",
                    VersionType = v.Type
                });
            }
            VersionGroups.Add(g);
        }
    }

    /// <summary>计算版本分组键：快照单独成组，其余取前两段主版本号（1.20.1 → 1.20），无法解析的归入旧版/其他。</summary>
    private static string GroupKey(string id, string bucket)
    {
        if (bucket == "snapshot") return "快照 & 预览";
        var m = Regex.Match(id, @"^(\d+)\.(\d+)");
        if (m.Success) return $"{m.Groups[1].Value}.{m.Groups[2].Value}";
        return bucket == "old" ? "旧版本" : "其他";
    }

    /// <summary>分组排序权重：0 快照 → 1 数字大版本 → 2 旧版本 → 3 其他。</summary>
    private static int GroupOrder(string header) =>
        header == "快照 & 预览" ? 0 : header is "旧版本" or "其他" ? (header == "旧版本" ? 2 : 3) : 1;

    /// <summary>从分组键（如 "1.20"）解析主/次版本号，供降序排序使用。</summary>
    private static int MajorMinor(string header)
    {
        var m = Regex.Match(header, @"^(\d+)\.(\d+)$");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var a) && int.TryParse(m.Groups[2].Value, out var b))
            return a * 1000 + b;
        return 0;
    }

    private async Task SearchModrinthAsync(CancellationToken ct = default)
    {
        var (type, fallback) = CurrentSubTab switch
        {
            "shader" => (ModrinthProjectType.Shader, "shader"),
            "resourcepack" => (ModrinthProjectType.ResourcePack, "tex"),
            "modpack" => (ModrinthProjectType.Modpack, "pack"),
            _ => (ModrinthProjectType.Mod, "mod")
        };

        var loader = ParseLoader(SelectedLoader);
        var gameVersion = string.IsNullOrEmpty(SelectedGameVersion) ? null : SelectedGameVersion;
        const int pageSize = 24;

        // bug #16：传入 offset/limit 实现翻页，并用远端 total_hits 计算总页数
        var (hits, total) = await LauncherService.Instance.SearchModsPagedAsync(
            Query, gameVersion, loader, type, pageSize, (Page - 1) * pageSize, ct);

        // bug #17：Modrinth 对中文分词支持极差，中文关键词常常返回一堆无关结果。
        // 先在本地按标题/slug/描述子串过滤；若本地过滤为空，再拉一批该类型的热门项目做本地匹配，
        // 保证中文关键词"搜得到"，而不是只能靠英文。
        var kw = (Query ?? "").Trim();
        if (kw.Length > 0 && HasCjk(kw))
        {
            var local = hits.Where(h => HitContains(h, kw)).ToList();
            if (local.Count == 0)
            {
                var (pool, poolTotal) = await LauncherService.Instance.SearchModsPagedAsync(
                    null, gameVersion, loader, type, 100, 0, ct);
                local = pool.Where(h => HitContains(h, kw)).ToList();
                // 回退命中时页码无远端含义：整批结果视为一页
                if (local.Count > 0) { hits = local; total = local.Count; Page = 1; }
                else hits = local;
            }
            else
            {
                hits = local;
                total = local.Count;
                Page = 1;
            }
        }

        ct.ThrowIfCancellationRequested();
        TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));

        foreach (var h in hits)
            Cards.Add(new DownloadCardItem
            {
                Id = h.ProjectId,
                Title = h.Title,
                Author = h.Slug,
                Summary = h.Description,
                IconUrl = string.IsNullOrEmpty(h.IconUrl) ? null : h.IconUrl,
                FallbackToken = fallback,
                Source = "Modrinth",
                SubTab = CurrentSubTab
            });
    }

    // ---- 整合包在线浏览（规格 2.2 → 整合包）----

    private async Task SearchModpackAsync(CancellationToken ct = default)
    {
        const int pageSize = 24;
        var items = await LauncherService.Instance.SearchModpacksAsync(
            Query,
            string.IsNullOrEmpty(SelectedGameVersion) ? null : SelectedGameVersion,
            SelectedLoader == "Any" ? null : SelectedLoader,
            _selectedModpackSource,
            ct, pageSize, (Page - 1) * pageSize);

        ct.ThrowIfCancellationRequested();

        // 整合包源不返回总命中数：本页被填满即认为还有下一页，否则当前页即末页（bug #16）。
        TotalPages = items.Count >= pageSize ? Page + 1 : Math.Max(1, Page);

        foreach (var it in items)
        {
            var meta = new List<string>();
            if (!string.IsNullOrEmpty(it.LatestGameVersion)) meta.Add("MC " + it.LatestGameVersion);
            if (it.Loaders.Count > 0) meta.Add(it.LoaderSummary);
            if (!string.IsNullOrEmpty(it.DownloadsText)) meta.Add(it.DownloadsText);

            Cards.Add(new DownloadCardItem
            {
                Id = it.Id,
                Title = it.Title,
                Author = it.Author,
                Summary = it.Summary,
                IconUrl = it.IconUrl,
                FallbackToken = "pack",
                Source = it.Source,
                SubTab = "modpack",
                MetaText = string.Join(" · ", meta)
            });
        }
    }

    private async Task SearchMapsAsync(bool resetPage = true, CancellationToken ct = default)
    {
        var client = LauncherService.Instance.Pixelmap;

        // 新搜索回到第一页；翻页时沿用 ChangePage 已改好的页码
        if (resetPage) Page = 1;

        var result = await client.SearchAsync(
            keyword: string.IsNullOrWhiteSpace(Query) ? null : Query,
            category: string.IsNullOrEmpty(SelectedCategory) ? null : SelectedCategory,
            version: string.IsNullOrEmpty(SelectedMapVersion) ? null : SelectedMapVersion,
            page: Page, limit: 24, sort: _mapSort);

        ct.ThrowIfCancellationRequested();

        TotalPages = result.PageCount;
        Page = result.Page;

        foreach (var it in result.Items)
            Cards.Add(new DownloadCardItem
            {
                Id = it.Slug,
                Title = it.Title,
                Author = it.Author,
                Summary = it.Summary,
                IconUrl = it.PreviewImage,
                FallbackToken = "map",
                Source = "PixelMap",
                SubTab = "map",
                Slug = it.Slug,
                Views = it.Views,
                VersionSummary = it.VersionSummary,
                CategorySummary = it.CategorySummary,
                CanDownload = it.DownloadAllowed,
                HasExtra = it.ExtensionExist
            });
    }

    /// <summary>宽松解析加载器名称：无法识别时回退 Any，避免 Enum.Parse 抛异常中断加入队列（bug #7）。</summary>
    private static LoaderType ParseLoader(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name == "Any") return LoaderType.Any;
        return Enum.TryParse<LoaderType>(name, ignoreCase: true, out var v) ? v : LoaderType.Any;
    }

    private void EnqueueSelected(DownloadCardItem? card)
    {
        try { EnqueueSelectedCore(card); }
        catch (Exception ex)
        {
            // bug #7：此前任何异常（目录解析 / 加载器解析失败）都会静默冒泡，
            // 按钮表现为"点了没反应"，任务永远挂不进队列。
            StatusMessage = $"加入队列失败：{ex.Message}";
        }
    }

    private void EnqueueSelectedCore(DownloadCardItem? card)
    {
        if (card is null) { StatusMessage = "请先选择一项"; return; }

        string dir;
        string kind;
        switch (card.SubTab)
        {
            case "shader":
                dir = PathEx.ShaderPacksDir(GameConstants.DefaultGameRoot);
                kind = "mod";
                break;
            case "resourcepack":
                dir = PathEx.ResourcePacksDir(GameConstants.DefaultGameRoot);
                kind = "mod";
                break;
            case "modpack":
                dir = Path.Combine(GameConstants.DefaultGameRoot, "modpacks");
                kind = "modpack";
                break;
            case "map":
                dir = PathEx.SavesDir(GameConstants.DefaultGameRoot);
                kind = "map";
                break;
            default:
                dir = PathEx.ModsDir(GameConstants.DefaultGameRoot);
                kind = "mod";
                break;
        }

        var loader = ParseLoader(SelectedLoader);
        Queue.Add(new DownloadQueueItem
        {
            ProjectId = card.Id,
            Title = card.Title,
            Summary = card.Summary,
            TargetDir = dir,
            GameVersion = string.IsNullOrEmpty(SelectedGameVersion) ? null : SelectedGameVersion,
            Loader = loader,
            Kind = kind,
            Slug = card.Slug,
            Source = card.SubTab == "modpack" ? card.Source : "modrinth"
        });

        StatusMessage = $"已加入队列：{card.Title}（共 {Queue.Count} 项）";
    }

    private async Task StartQueueAsync()
    {
        IsBusy = true;
        try
        {
            foreach (var item in Queue.Where(q => q.Status is "排队中" or "已暂停").ToList())
            {
                item.Cts = new CancellationTokenSource();
                item.Status = item.Kind == "version" ? "安装中" : "下载中";
                item.Progress = 0;

                var local = item;
                var ok = false;
                try
                {
                    ok = local.Kind switch
                    {
                        "modpack" => (await LauncherService.Instance.InstallModpackAsync(
                            local.Source, local.ProjectId, local.GameVersion, local.Title,
                            Progress(local), local.Cts.Token)) is not null,
                        "map" => await LauncherService.Instance.DownloadMapAsync(
                            local.Slug ?? "", Progress(local), local.Cts.Token),
                        "version" => (await LauncherService.Instance.InstallVersionAsync(
                            local.ProjectId, local.InstallLoader, Progress(local), local.Cts.Token)) is not null,
                        // bug #14：详情页指定了版本时按该版本的文件直链下载，避免装到自动挑选的其它版本
                        _ => !string.IsNullOrEmpty(local.FileUrl) && !string.IsNullOrEmpty(local.FileName)
                            ? await LauncherService.Instance.DownloadModFileAsync(
                                local.FileUrl!, local.FileName!, local.FileSha1, local.TargetDir,
                                Progress(local), local.Cts.Token)
                            : await LauncherService.Instance.DownloadModAsync(
                                local.ProjectId, local.TargetDir, local.GameVersion, local.Loader,
                                Progress(local), local.Cts.Token)
                    };

                    if (local.Cts.Token.IsCancellationRequested && local.Status != "已暂停")
                        local.Status = "已取消";
                    else
                        local.Status = ok ? "已完成" : "失败";
                }
                catch (OperationCanceledException)
                {
                    local.Status = "已取消";
                }
                catch
                {
                    local.Status = "失败";
                }

                local.Progress = local.Status == "已完成" ? 100 : local.Progress;
            }

            StatusBarViewModel.Current.DownloadText = "下载队列完成";
            StatusBarViewModel.Current.DownloadProgress = 0;

            // 完成后自动清空已完成/已取消/失败项，避免队列里残留历史任务，同时清掉标题栏红点角标。
            CleanupFinishedItems();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>清理已结束（已完成 / 已取消 / 失败）的队列项。</summary>
    private void CleanupFinishedItems()
    {
        foreach (var it in Queue.Where(q => q.Status is "已完成" or "已取消" or "失败").ToList())
            Queue.Remove(it);
    }

    private static IProgress<double> Progress(DownloadQueueItem item) =>
        new Progress<double>(p =>
        {
            item.Progress = p * 100;
            StatusBarViewModel.Current.DownloadProgress = p * 100;
            StatusBarViewModel.Current.DownloadText = $"下载 {item.Title}：{p:P0}";
        });

    private void PauseItem(DownloadQueueItem? item)
    {
        if (item is null) return;
        if (item.Status is "排队中" or "下载中")
        {
            item.Cts?.Cancel();
            item.Status = "已暂停";
        }
    }

    private void CancelItem(DownloadQueueItem? item)
    {
        if (item is null) return;
        if (item.Status is "排队中" or "下载中" or "已暂停")
        {
            item.Cts?.Cancel();
            item.Status = "已取消";
        }
    }

    private void OpenDetail(DownloadCardItem? card)
    {
        if (card is null) return;

        if (card.SubTab == "modpack")
        {
            _ = OpenModpackDetailAsync(card);
            return;
        }

        if (card.Source == "Minecraft")
        {
            // 打开加载器选择弹窗
            CurrentInstallVersion = card;
            SelectedInstallLoader = "none";
            IsInstallOpen = true;
            return;
        }

        if (card.Source == "PixelMap")
        {
            _ = LoadMapDetailAsync(card);
        }
        else if (!string.IsNullOrEmpty(card.Id))
        {
            // bug #14：Modrinth 项目（mod / 光影 / 资源包）改为在启动器内打开页内详情，
            // 可返回、可选版本、可 AI 翻译；浏览器入口仍在详情弹窗内保留。
            _ = OpenProjectDetailAsync(card);
        }
    }

    /// <summary>
    /// bug #14：拉取项目版本并打开页内详情弹窗。
    /// 先用卡片已有信息（标题 / 封面 / 描述）立即渲染，版本列表异步补上，避免弹窗空白等待。
    /// </summary>
    private async Task OpenProjectDetailAsync(DownloadCardItem card)
    {
        CurrentProjectDetail = new ModrinthProjectDetail
        {
            Id = card.Id,
            Title = card.Title,
            Author = card.Author,
            Description = card.Summary,
            IconUrl = card.IconUrl ?? "",
            FallbackToken = card.FallbackToken,
            SubTab = card.SubTab ?? "mod",
            ProjectUrl = $"https://modrinth.com/project/{card.Id}"
        };
        SelectedProjectVersion = null;
        ProjectTranslated = "";
        ProjectDetailHint = "正在加载版本…";
        IsProjectDetailOpen = true;

        try
        {
            var versions = await LauncherService.Instance.GetProjectVersionChoicesAsync(
                card.Id,
                string.IsNullOrEmpty(SelectedGameVersion) ? null : SelectedGameVersion,
                ParseLoader(SelectedLoader));

            var detail = CurrentProjectDetail;
            if (detail is null) return;
            detail.Versions = versions;
            SelectedProjectVersion = versions.FirstOrDefault();

            // 关闭弹窗后迟到的响应不应再改动 UI
            if (!IsProjectDetailOpen) return;

            ProjectDetailHint = versions.Count == 0
                ? "该项目没有与当前筛选（游戏版本 / 加载器）匹配的版本文件"
                : "";
            OnPropertyChanged(nameof(CurrentProjectDetail));
            OnPropertyChanged(nameof(ProjectDetailHasVersions));
            OnPropertyChanged(nameof(ProjectDetailLoaders));
        }
        catch (Exception ex)
        {
            ProjectDetailHint = $"版本加载失败：{ex.Message}";
        }
    }

    /// <summary>把详情页所选版本加入下载队列并自动开始（bug #14）。</summary>
    private async Task InstallProjectVersionAsync()
    {
        var detail = CurrentProjectDetail;
        var ver = SelectedProjectVersion;
        if (detail is null || ver is null) { ProjectDetailHint = "请先选择一个版本"; return; }

        var dir = detail.SubTab switch
        {
            "shader" => PathEx.ShaderPacksDir(GameConstants.DefaultGameRoot),
            "resourcepack" => PathEx.ResourcePacksDir(GameConstants.DefaultGameRoot),
            _ => PathEx.ModsDir(GameConstants.DefaultGameRoot)
        };

        Queue.Add(new DownloadQueueItem
        {
            ProjectId = detail.Id,
            Title = detail.Title,
            Summary = ver.DisplayText,
            TargetDir = dir,
            GameVersion = string.IsNullOrEmpty(SelectedGameVersion) ? null : SelectedGameVersion,
            Loader = ParseLoader(SelectedLoader),
            Kind = "mod",
            Source = "modrinth",
            // 指定版本 → 直链下载该文件，避免装到自动挑选的其它版本
            FileUrl = ver.FileUrl,
            FileName = ver.FileName,
            FileSha1 = ver.FileSha1
        });

        ProjectDetailHint = $"已加入队列：{detail.Title} · {ver.VersionNumber}";
        StatusMessage = $"已加入队列：{detail.Title}（共 {Queue.Count} 项）";
        await StartQueueAsync();
    }

    /// <summary>用 AI 翻译项目描述（bug #14）。</summary>
    private async Task TranslateDetailAsync()
    {
        var detail = CurrentProjectDetail;
        if (detail is null) return;
        if (string.IsNullOrWhiteSpace(detail.Description))
        {
            ProjectDetailHint = "该项目没有可翻译的描述";
            return;
        }

        ProjectDetailHint = "AI 翻译中…";
        try
        {
            var text = await Chert.Core.Ai.Assistant.TranslateModDescriptionAsync(detail.Description);
            ProjectTranslated = text ?? "";
            ProjectDetailHint = AiEnabled ? "翻译完成" : "AI 未启用，已返回原文";
        }
        catch (Exception ex)
        {
            ProjectDetailHint = $"翻译失败：{ex.Message}";
        }
    }

    /// <summary>在浏览器打开项目页（详情页保留的外部入口）。</summary>
    private void OpenProjectPage()
    {
        var url = CurrentProjectDetail?.ProjectUrl;
        if (string.IsNullOrEmpty(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { ProjectDetailHint = "无法打开外部浏览器"; }
    }

    /// <summary>把选中的 Minecraft 版本（含加载器）加入底部下载队列，随后自动开始队列。
    /// fabric 由 FabricInstaller 自动配对最新 Fabric API。</summary>
    private void EnqueueVersionInstall()
    {
        if (CurrentInstallVersion is null) return;
        var card = CurrentInstallVersion;
        var loader = SelectedInstallLoader;

        Queue.Add(new DownloadQueueItem
        {
            ProjectId = card.Id,
            Title = card.Title,
            Summary = LoaderLabel(loader),
            TargetDir = GameConstants.DefaultGameRoot,
            Loader = LoaderType.Any,
            Kind = "version",
            InstallLoader = loader
        });

        StatusMessage = $"已加入安装队列：Minecraft {card.Title}（{LoaderLabel(loader)}）";
        IsInstallOpen = false;

        // 队列空闲则自动开始（若正在下载，新项随当前轮次一并处理）
        if (!IsBusy) _ = StartQueueAsync();
    }

    private static string LoaderLabel(string loader) => loader switch
    {
        "fabric" => "Fabric（自动配对最新 Fabric API）",
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        "quilt" => "Quilt",
        _ => "原版（无加载器）"
    };

    // ---- 整合包详情弹窗 ----

    private async Task OpenModpackDetailAsync(DownloadCardItem card)
    {
        IsBusy = true;
        try
        {
            var detail = await LauncherService.Instance.GetModpackDetailAsync(card.Source, card.Id, CancellationToken.None);
            if (detail is null) { StatusMessage = "无法获取整合包详情"; return; }

            CurrentModpackSourceId = card.Source;
            CurrentModpackDetail = detail;
            SelectedModpackVersion = detail.Versions.FirstOrDefault();
            InstallIsolated = true;

            ModpackDetailHint = detail.HasVersions
                ? (ModpackDetailCanInstall
                    ? ""
                    : "该版本无可用的直链下载。")
                : "该整合包暂无可安装版本。";

            IsModpackDetailOpen = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"整合包详情加载失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>按详情窗所选版本安装整合包（支持隔离安装）。</summary>
    private async Task InstallModpackAsync()
    {
        if (CurrentModpackDetail is null || SelectedModpackVersion is null) return;
        IsBusy = true;
        ModpackDetailHint = "正在安装整合包…";
        try
        {
            var progress = new Progress<double>(p =>
            {
                StatusBarViewModel.Current.DownloadProgress = p * 100;
                StatusBarViewModel.Current.DownloadText = $"安装 {CurrentModpackDetail.Title}：{p:P0}";
            });

            var isolated = InstallIsolated;
            var result = await LauncherService.Instance.InstallModpackVersionAsync(
                CurrentModpackSourceId, SelectedModpackVersion, isolated,
                CurrentModpackDetail.Title, progress, CancellationToken.None);

            if (result is null)
            {
                ModpackDetailHint = "安装失败：所选版本无可用下载链接。";
                StatusMessage = "整合包安装失败";
                return;
            }

            var where = result.Isolated ? $"隔离目录 versions/{result.VersionId}" : "共享目录";
            var renamed = result.Renamed ? "（因重名已自动改名）" : "";
            ModpackDetailHint = $"已安装：{result.Name} → {where}{renamed}（{result.ModCount} 个 Mod）";
            StatusMessage = $"整合包已安装：{result.Name}";
        }
        catch (Exception ex)
        {
            ModpackDetailHint = $"安装失败：{ex.Message}";
            StatusMessage = $"整合包安装失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>在系统浏览器打开当前整合包的站内详情页。</summary>
    private void OpenModpackPage()
    {
        var url = CurrentModpackDetail?.PageUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            StatusMessage = "无法打开外部浏览器";
        }
    }

    private async Task LoadMapDetailAsync(DownloadCardItem card)
    {
        IsBusy = true;
        try
        {
            var detail = await LauncherService.Instance.Pixelmap.GetDetailAsync(card.Slug ?? "", CancellationToken.None);
            if (detail is null) { StatusMessage = "无法获取地图详情"; return; }

            CurrentMapDetail = detail;
            DetailHint = detail.CanDownload
                ? detail.HasAdditionalResources
                    ? "该地图附带资源包 / 光影，可一并安装。"
                    : ""
                : "作者未授权启动器直接下载，请前往地图站页面获取。";
            IsDetailOpen = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"详情加载失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DownloadDetailAsync()
    {
        if (CurrentMapDetail is null) return;
        IsBusy = true;
        DetailHint = "正在下载地图…";
        try
        {
            var ok = await LauncherService.Instance.DownloadMapAsync(CurrentMapDetail.Slug, null, CancellationToken.None);
            StatusMessage = ok ? $"地图已安装：{CurrentMapDetail.Title}" : "地图下载/安装失败";
            DetailHint = ok
                ? $"已解压进 saves：{CurrentMapDetail.Title}"
                : "地图下载或解压失败，可尝试打开地图站页面手动下载。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"地图下载失败：{ex.Message}";
            DetailHint = $"下载失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 下载附加资源并按类型分发：资源包进 resourcepacks、光影进 shaderpacks、
    /// 数据包暂存 downloads/extras/datapacks（需用户自行放入具体存档）。
    /// </summary>
    private async Task DownloadExtraAsync()
    {
        if (CurrentMapDetail is null) return;
        IsBusy = true;
        DetailHint = "正在下载附加资源…";
        try
        {
            var result = await LauncherService.Instance.DownloadMapExtraAsync(
                CurrentMapDetail, null, CancellationToken.None);

            if (result is null)
            {
                DetailHint = "该地图没有提供附加资源直链。";
                StatusMessage = "无附加资源";
                return;
            }

            if (!result.Ok)
            {
                DetailHint = $"附加资源安装失败：{result.Error}";
                StatusMessage = "附加资源安装失败";
                return;
            }

            var hint = $"附加资源已安装：{result.Summary}";
            if (result.HasUnknown)
                hint += "（部分文件未能识别类型，已保留在 downloads/extras）";
            DetailHint = hint;
            StatusMessage = hint;
        }
        catch (Exception ex)
        {
            DetailHint = $"附加资源下载失败：{ex.Message}";
            StatusMessage = $"附加资源下载失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>在系统浏览器打开当前地图的站内详情页。</summary>
    private void OpenMapPage()
    {
        var url = CurrentMapDetail?.PageUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            StatusMessage = "无法打开外部浏览器";
        }
    }
}
