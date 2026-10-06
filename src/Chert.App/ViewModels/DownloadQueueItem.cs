using Chert.Core.Models;
using Chert.Core.Mvvm;

namespace Chert.App.ViewModels;

/// <summary>搜索结果条目（卡片数据）。</summary>
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

    /// <summary>用户在详情页指定了具体版本时的主文件直链（非空时直接下载该文件）。</summary>
    public string? FileUrl { get; init; }

    /// <summary>配合 <see cref="FileUrl"/> 的落地文件名。</summary>
    public string? FileName { get; init; }

    /// <summary>配合 <see cref="FileUrl"/> 的 SHA1 校验值（可为空）。</summary>
    public string? FileSha1 { get; init; }

    public CancellationTokenSource? Cts { get; set; }

    /// <summary>失败原因（此前 catch 里只把状态写成「失败」，异常信息被丢掉）。</summary>
    public string ErrorMessage { get; set; } = "";

    /// <summary>
    /// 去重键：同一项目 + 同一目标目录 + 同一版本/加载器 + 同一文件直链视为同一项。
    /// 详情版本直链（<see cref="FileUrl"/>）参与比较，避免「同一项目的两个版本」被误判为重复。
    /// </summary>
    public string DedupKey =>
        $"{Kind}|{ProjectId}|{TargetDir}|{GameVersion ?? ""}|{Loader}|{InstallLoader}|{FileUrl ?? ""}";

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

    /// <summary>已尝试次数（首次为 1）。</summary>
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

    /// <summary>暂停 / 继续 / 重试按钮的可用性（随状态变化）。</summary>
    public bool CanPause => _status is "排队中" or "下载中";
    public bool CanResume => _status is "已暂停";
    public bool CanRetry => _status is "失败" or "已取消";

    /// <summary>是否仍在处理中（去重用：已失败 / 已取消的项允许重新入队）。</summary>
    public bool IsActive => _status is "排队中" or "下载中" or "已暂停" or "已完成";

    private double _progress;

    public double Progress
    {
        get => _progress;
        set => SetField(ref _progress, value);
    }
}
