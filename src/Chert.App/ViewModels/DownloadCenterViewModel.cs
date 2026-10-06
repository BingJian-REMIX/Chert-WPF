using System.Collections.ObjectModel;

using System.Windows.Input;

using Chert.Core.Download;

using Chert.Core.Models;

using Chert.Core.Mvvm;

using Chert.Core.Profiles;

using Chert.Core.Utils;

using Chert.App.Services;

using Chert.App.ViewModels;



namespace Chert.App.ViewModels;



public class SearchResultEntry

{

    public string ProjectId { get; init; } = "";

    public string Title { get; init; } = "";

    public string Summary { get; init; } = "";



    /// <summary>下载种类：mod（含 shader/resourcepack）/ modpack（整合包）/ map（地图）。</summary>

    public string Kind { get; init; } = "mod";



    /// <summary>地图 slug（Kind=map 时用于回查详情直链）。</summary>

    public string? Slug { get; init; }

}



/// <summary>下载队列中的一项。</summary>

public class DownloadQueueItem : ObservableObject

{

    public string ProjectId { get; init; } = "";

    public string Title { get; init; } = "";

    public string TargetDir { get; init; } = "";

    public string? GameVersion { get; init; }

    public LoaderType Loader { get; init; }



    /// <summary>队列项摘要（卡片副标题），用于队列列表二行展示。</summary>

    public string Summary { get; init; } = "";



    /// <summary>整合包来源（modrinth），仅 Kind=modpack 使用。</summary>

    public string Source { get; init; } = "modrinth";



    /// <summary>

    /// 队列项类别，决定执行时走哪条下载/安装路径：

    /// mod / shader / resourcepack（Modrinth 文件下载）、modpack（整合包）、map（像素茶艺地图）、

    /// version（Minecraft 版本安装，配合 <see cref="InstallLoader"/>）。

    /// </summary>

    public string Kind { get; init; } = "mod";



    /// <summary>地图 slug（Kind=map 时用于回查详情直链）。</summary>

    public string? Slug { get; init; }



    /// <summary>版本安装所选加载器（none / forge / fabric / neoforge / quilt），仅 Kind=version 使用。</summary>

    public string InstallLoader { get; init; } = "none";



    /// <summary>

    /// bug #14：用户在详情页指定了具体版本时的主文件直链。

    /// 非空时队列直接下载该文件，而不是让服务端按 gameVersion/loader 自动挑一个

    /// （此前详情页只能「加入队列」，实际装的不一定是所选版本）。

    /// </summary>

    public string? FileUrl { get; init; }



    /// <summary>配合 <see cref="FileUrl"/> 的落地文件名。</summary>

    public string? FileName { get; init; }



    /// <summary>配合 <see cref="FileUrl"/> 的 SHA1 校验值（可为空）。</summary>

    public string? FileSha1 { get; init; }



    public CancellationTokenSource? Cts { get; set; }



    /// <summary>
    /// 失败原因。此前 catch 里只把状态写成「失败」，异常信息被丢掉，
    /// 用户只知道没下成，不知道为什么。
    /// </summary>
    public string ErrorMessage { get; set; } = "";

    private string _status = "排队中";

    public string Status

    {

        get => _status;

        set

        {

            if (!SetField(ref _status, value)) return;

            OnPropertyChanged(nameof(CanPause));

            OnPropertyChanged(nameof(CanResume));

            OnPropertyChanged(nameof(CanRetry));

        }

    }

    /// <summary>清单 #67：已尝试次数（首次为 1）。</summary>

    public int Attempts

    {

        get => _attempts;

        set

        {

            if (!SetField(ref _attempts, value)) return;

            OnPropertyChanged(nameof(AttemptsText));

        }

    }

    private int _attempts;

    /// <summary>重试次数展示（&gt;1 时才显示）。</summary>

    public string AttemptsText => _attempts > 1 ? $"（第 {_attempts} 次）" : "";

    /// <summary>清单 #67：暂停 / 继续 / 重试按钮的可用性（随状态变化）。</summary>

    public bool CanPause => _status is "排队中" or "下载中";

    public bool CanResume => _status is "已暂停";

    public bool CanRetry => _status is "失败" or "已取消";



    private double _progress;

    public double Progress

    {

        get => _progress;

        set => SetField(ref _progress, value);

    }

}



public class DownloadCenterViewModel : ObservableObject

{

    private string _query = "";

    private string _selectedLoader = "Any";

    private string _selectedGameVersion = "";

    private string _selectedProjectType = "mod";

    private ObservableCollection<SearchResultEntry> _results = new();

    private SearchResultEntry? _selectedResult;

    private string _statusMessage = "";

    private bool _isBusy;



    private ObservableCollection<DownloadQueueItem> _queue = new();



    // ===== 清单 #67：下载队列管理（暂停 / 继续 / 限速 / 失败重试）=====

    /// <summary>限速档位（KB/s，0 = 不限速）。改动即写入全局 DownloadSpeedLimiter，对所有下载生效。</summary>

    public static ObservableCollection<int> SpeedLimitChoices { get; } = new() { 0, 256, 512, 1024, 2048, 5120 };

    /// <summary>自动重试次数档位（0 = 不重试）。</summary>

    public static ObservableCollection<int> RetryChoices { get; } = new() { 0, 1, 2, 3, 5 };

    private int _speedLimitKbps;

    public int SpeedLimitKbps

    {

        get => _speedLimitKbps;

        set

        {

            if (!SetField(ref _speedLimitKbps, Math.Max(0, value))) return;

            DownloadSpeedLimiter.SetKilobytesPerSecond(_speedLimitKbps);

            SaveDownloadPrefs();

        }

    }

    private int _autoRetryCount = 2;

    public int AutoRetryCount

    {

        get => _autoRetryCount;

        set

        {

            if (!SetField(ref _autoRetryCount, Math.Clamp(value, 0, 10))) return;

            SaveDownloadPrefs();

        }

    }

    /// <summary>队列是否被整体暂停（继续队列时清除）。</summary>

    public bool QueuePaused

    {

        get => _queuePaused;

        set

        {

            if (!SetField(ref _queuePaused, value)) return;

            OnPropertyChanged(nameof(QueuePausedText));

        }

    }

    private bool _queuePaused;

    public string QueuePausedText => _queuePaused ? "（队列已暂停）" : "";

    public ICommand ResumeItemCommand { get; }

    public ICommand RetryItemCommand { get; }

    public ICommand PauseAllCommand { get; }

    public ICommand ResumeAllCommand { get; }

    private void SaveDownloadPrefs()

    {

        try

        {

            var p = ProfileStore.Load(GameConstants.DefaultGameRoot);

            p.DownloadSpeedLimitKbps = _speedLimitKbps;

            p.DownloadAutoRetryCount = _autoRetryCount;

            ProfileStore.Save(p);

        }

        catch { /* 偏好保存失败不影响下载 */ }

    }

    public ObservableCollection<string> Loaders { get; } = new() { "Any", "Fabric", "Forge", "Quilt" };

    public ObservableCollection<string> ProjectTypes { get; } = new() { "mod", "shader", "resourcepack" };

    public ObservableCollection<GameVersionItem> GameVersions { get; } = new() { new GameVersionItem() };



    public string Query

    {

        get => _query;

        set => SetField(ref _query, value);

    }



    public string SelectedLoader

    {

        get => _selectedLoader;

        set => SetField(ref _selectedLoader, value);

    }



    public string SelectedGameVersion

    {

        get => _selectedGameVersion;

        set => SetField(ref _selectedGameVersion, value);

    }



    public string SelectedProjectType

    {

        get => _selectedProjectType;

        set => SetField(ref _selectedProjectType, value);

    }



    public ObservableCollection<SearchResultEntry> Results

    {

        get => _results;

        set => SetField(ref _results, value);

    }



    public SearchResultEntry? SelectedResult

    {

        get => _selectedResult;

        set => SetField(ref _selectedResult, value);

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



    public ObservableCollection<DownloadQueueItem> Queue

    {

        get => _queue;

        set => SetField(ref _queue, value);

    }



    /// <summary>队列项数（驱动标题栏下载按钮角标与弹窗「共 N 项」）。</summary>
    public int QueueCount => _queue.Count;

    /// <summary>队列是否非空（驱动标题栏下载按钮环形进度与角标显隐）。</summary>
    public bool HasQueue => _queue.Count > 0;

    public ICommand LoadVersionsCommand { get; }

    public ICommand SearchCommand { get; }

    public ICommand EnqueueCommand { get; }

    public ICommand StartQueueCommand { get; }

    public ICommand PauseItemCommand { get; }

    public ICommand CancelItemCommand { get; }

    public ICommand RemoveItemCommand { get; }

    public ICommand ClearCompletedCommand { get; }



    public DownloadCenterViewModel()

    {

        LoadVersionsCommand = new AsyncRelayCommand(_ => LoadGameVersionsAsync());

        SearchCommand = new AsyncRelayCommand(_ => SearchAsync(), _ => !IsBusy);

        EnqueueCommand = new RelayCommand(_ => Enqueue());

        StartQueueCommand = new AsyncRelayCommand(_ => StartQueueAsync(), _ => !IsBusy);

        PauseItemCommand = new RelayCommand(p => PauseItem(p as DownloadQueueItem));

        // 清单 #67：真正的「继续 / 重试」——此前暂停即取消，暂停后无法再继续

        ResumeItemCommand = new RelayCommand(p => ResumeItem(p as DownloadQueueItem));

        RetryItemCommand = new RelayCommand(p => ResumeItem(p as DownloadQueueItem));

        PauseAllCommand = new RelayCommand(_ => PauseAll());

        ResumeAllCommand = new RelayCommand(_ => ResumeAll());

        var profile = ProfileStore.Load(GameConstants.DefaultGameRoot);

        _speedLimitKbps = profile.DownloadSpeedLimitKbps;

        _autoRetryCount = profile.DownloadAutoRetryCount;

        if (!SpeedLimitChoices.Contains(_speedLimitKbps)) SpeedLimitChoices.Add(_speedLimitKbps);

        if (!RetryChoices.Contains(_autoRetryCount)) RetryChoices.Add(_autoRetryCount);

        DownloadSpeedLimiter.SetKilobytesPerSecond(_speedLimitKbps);

        CancelItemCommand = new RelayCommand(p => CancelItem(p as DownloadQueueItem));

        RemoveItemCommand = new RelayCommand(p => RemoveItem(p as DownloadQueueItem));

        ClearCompletedCommand = new RelayCommand(_ => ClearCompleted());
        _queue.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(QueueCount));
            OnPropertyChanged(nameof(HasQueue));
        };

        _ = LoadGameVersionsAsync();

    }



    private async Task LoadGameVersionsAsync()

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

    }



    private async Task SearchAsync()

    {

        IsBusy = true;

        try

        {

            var loader = SelectedLoader == "Any" ? LoaderType.Any : Enum.Parse<LoaderType>(SelectedLoader);

            var type = SelectedProjectType switch

            {

                "shader" => ModrinthProjectType.Shader,

                "resourcepack" => ModrinthProjectType.ResourcePack,

                _ => ModrinthProjectType.Mod

            };

            var list = await LauncherService.Instance.SearchModsAsync(Query, string.IsNullOrEmpty(SelectedGameVersion) ? null : SelectedGameVersion, loader, type);

            Results = new ObservableCollection<SearchResultEntry>(

                list.Select(h => new SearchResultEntry { ProjectId = h.ProjectId, Title = h.Title, Summary = h.Description }));

            StatusMessage = Results.Count > 0 ? $"找到 {Results.Count} 个结果" : "未找到结果";

        }

        catch (Exception ex)

        {

            StatusMessage = $"搜索失败：{ex.Message}";

        }

        finally

        {

            IsBusy = false;

        }

    }



    private void Enqueue()

    {

        if (SelectedResult is null) { StatusMessage = "请先选择一个结果"; return; }

        var targetDir = SelectedProjectType switch

        {

            "shader" => PathEx.ShaderPacksDir(GameConstants.DefaultGameRoot),

            "resourcepack" => PathEx.ResourcePacksDir(GameConstants.DefaultGameRoot),

            _ => PathEx.ModsDir(GameConstants.DefaultGameRoot)

        };

        var loader = SelectedLoader == "Any" ? LoaderType.Any : Enum.Parse<LoaderType>(SelectedLoader);

        Queue.Add(new DownloadQueueItem

        {

            ProjectId = SelectedResult.ProjectId,

            Title = SelectedResult.Title,

            TargetDir = targetDir,

            GameVersion = string.IsNullOrEmpty(SelectedGameVersion) ? null : SelectedGameVersion,

            Loader = loader

        });

        StatusMessage = $"已加入队列：{SelectedResult.Title}（共 {Queue.Count} 项）";

    }



    private async Task StartQueueAsync()

    {

        IsBusy = true;

        QueuePaused = false;

        try

        {

            // 清单 #67：只取「排队中」；已暂停的项需显式「继续」才会重新入队

            foreach (var item in Queue.Where(q => q.Status == "排队中").ToList())

            {

                // 整体暂停：当前项跑完即停，后续项保持排队中，可随时继续

                if (_queuePaused) break;

                item.Cts = new CancellationTokenSource();

                item.Status = "下载中";

                item.Progress = 0;

                var maxAttempts = Math.Max(1, _autoRetryCount + 1);

                var attempt = 0;

                while (attempt < maxAttempts)

                {

                    attempt++;

                    item.Attempts = attempt;

                    var last = attempt == maxAttempts;

                    try

                    {

                        var local = item;

                        var ok = await LauncherService.Instance.DownloadModAsync(

                            item.ProjectId, item.TargetDir, item.GameVersion, item.Loader,

                            new Progress<double>(p =>

                            {

                                local.Progress = p * 100;

                                StatusBarViewModel.Current.DownloadProgress = p * 100;

                                StatusBarViewModel.Current.DownloadText = $"下载 {local.Title}：{p:P0}";

                            }),

                            item.Cts.Token);

                        if (item.Cts.Token.IsCancellationRequested)

                        {

                            if (item.Status != "已暂停") item.Status = "已取消";

                            break;

                        }

                        if (ok)

                        {

                            item.Status = "已完成";

                            break;

                        }

                        if (last) { item.Status = "失败"; break; }

                    }

                    catch (OperationCanceledException)

                    {

                        if (item.Status != "已暂停") item.Status = "已取消";

                        break;

                    }

                    catch

                    {

                        if (last) { item.Status = "失败"; break; }

                    }

                    // 清单 #67：失败自动重试（退避 2s × 次数），期间状态可见

                    item.Status = $"失败，{2 * attempt} 秒后重试（{attempt}/{maxAttempts - 1}）";

                    try { await Task.Delay(2000 * attempt, item.Cts.Token); }

                    catch { /* 取消则直接进入下一轮判断 */ }

                    if (item.Cts.Token.IsCancellationRequested)

                    {

                        if (item.Status != "已暂停") item.Status = "已取消";

                        break;

                    }

                }

                item.Progress = item.Status == "已完成" ? 100 : item.Progress;

            }

            StatusBarViewModel.Current.DownloadText = _queuePaused ? "下载队列已暂停" : "下载队列完成";

            if (!_queuePaused)

            {

                StatusBarViewModel.Current.DownloadProgress = 0;

                ClearCompleted();

            }

        }

        finally

        {

            IsBusy = false;

        }

    }



    /// <summary>清单 #67：暂停单项。下载中的项取消当前请求并标记「已暂停」，排队中的项直接移出待取集合。</summary>

    private void PauseItem(DownloadQueueItem? item)

    {

        if (item is null) return;

        if (item.Status is "下载中" or "排队中")

        {

            item.Status = "已暂停";

            item.Cts?.Cancel();

        }

    }

    /// <summary>清单 #67：继续 / 重试单项——重新置为排队中并启动队列（空闲时）。</summary>

    private void ResumeItem(DownloadQueueItem? item)

    {

        if (item is null) return;

        if (item.Status is not ("已暂停" or "失败" or "已取消")) return;

        item.Status = "排队中";

        item.Progress = 0;

        item.Attempts = 0;

        if (!IsBusy) _ = StartQueueAsync();

    }

    /// <summary>清单 #67：继续队列——把暂停项重新置为排队中并启动（StartQueueAsync 会清除暂停标记）。</summary>

    private void ResumeAll()

    {

        foreach (var it in Queue)

            if (it.Status == "已暂停")

            {

                it.Status = "排队中";

                it.Attempts = 0;

            }

        if (!IsBusy) _ = StartQueueAsync();

    }

    /// <summary>清单 #67：整体暂停——当前项取消，队列跑完当前项后停住。</summary>

    private void PauseAll()

    {

        QueuePaused = true;

        foreach (var it in Queue)

            if (it.Status == "下载中")

            {

                it.Status = "已暂停";

                it.Cts?.Cancel();

            }

        StatusMessage = "队列已暂停，可点击「继续队列」接着下载";

    }



    private void CancelItem(DownloadQueueItem? item)

    {

        if (item is null) return;

        item.Cts?.Cancel();

        item.Status = "已取消";

    }



    // bug #7：下载完成后任务仍留在队列（重启才重置）。允许手动移除任意队列项，

    // 并提供「清空已完成」一键清理已结束（完成/取消/失败）的项。

    private void RemoveItem(DownloadQueueItem? item)

    {

        if (item is null) return;

        item.Cts?.Cancel();

        Queue.Remove(item);

    }



    private void ClearCompleted()

    {

        foreach (var it in Queue.Where(q => q.Status is "已完成" or "已取消" or "失败").ToList())

            Queue.Remove(it);

    }

}

