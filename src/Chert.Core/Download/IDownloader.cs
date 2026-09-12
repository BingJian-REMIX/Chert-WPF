namespace Chert.Core.Download;

/// <summary>单个下载任务。</summary>
public class DownloadItem
{
    /// <summary>候选镜像 URL（按优先级尝试，前一个失败自动切换下一个）。</summary>
    public List<string> Urls { get; set; } = new();

    /// <summary>本地保存路径。</summary>
    public string Destination { get; set; } = "";

    /// <summary>期望的 SHA-1（可选，用于校验）。</summary>
    public string? ExpectedSha1 { get; set; }

    /// <summary>期望的文件大小（字节，可选）。</summary>
    public long? ExpectedSize { get; set; }

    public DownloadItem() { }

    public DownloadItem(IEnumerable<string> urls, string destination, string? sha1 = null, long? size = null)
    {
        Urls = urls.ToList();
        Destination = destination;
        ExpectedSha1 = sha1;
        ExpectedSize = size;
    }
}

/// <summary>下载器接口。</summary>
public interface IDownloader
{
    /// <summary>下载单个文件（带镜像回退与校验）。</summary>
    Task DownloadAsync(DownloadItem item, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>并行下载一批文件，进度回传 (已完成, 总数)。</summary>
    Task DownloadBatchAsync(IEnumerable<DownloadItem> items,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default);
}
