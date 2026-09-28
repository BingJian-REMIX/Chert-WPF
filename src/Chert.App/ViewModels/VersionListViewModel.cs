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

    /// <summary>bug #10：请求以大页形式打开某版本的版本设置（由 VersionListView 订阅并导航）。</summary>
    public event Action<VersionEntry>? SettingsRequested;

    public VersionListViewModel()
    {
        RefreshCommand = new RelayCommand(_ => Refresh());
        LaunchCommand = new AsyncRelayCommand(_ => LaunchAsync(), _ => CanLaunch);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
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

        Versions = list;
        StatusMessage = Versions.Count > 0
            ? $"共发现 {Versions.Count} 个版本"
            : "暂无已安装版本，请前往「安装新版本」";

        // 恢复上次选中的版本（持久化在 profile.LastVersionId），使快速启动下拉在刷新后实时回显选中态
        var lastId = "";
        try { lastId = ProfileStore.Load(LauncherService.Instance.GameRoot).LastVersionId ?? ""; } catch { }
        SelectedVersion = Versions.FirstOrDefault(v => v.Id == lastId) ?? Versions.FirstOrDefault();

        // 缺失前置扫描要解析每个 Mod 的 jar 元数据，较慢；放后台逐个回填，
        // 列表先出内容、徽章随后补上，避免刷新卡顿。
        _ = ScanPrerequisitesAsync(list.ToList(), gameRoot);
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
                    .InvokeAsync(() => entry.SetMissingPrerequisites(missing));
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
