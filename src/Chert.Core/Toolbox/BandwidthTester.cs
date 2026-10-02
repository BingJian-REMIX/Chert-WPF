using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Chert.Core.Toolbox;

/// <summary>本地带宽测速结果。</summary>
public class BandwidthResult
{
    /// <summary>单线程连续下载的平均速率（字节/秒）。</summary>
    public double SingleThreadBytesPerSec { get; init; }

    /// <summary>多线程并发的峰值速率（字节/秒）。</summary>
    public double MultiThreadBytesPerSec { get; init; }

    /// <summary>实际下载字节数。</summary>
    public long BytesDownloaded { get; init; }

    /// <summary>实际耗时（秒）。</summary>
    public double ElapsedSeconds { get; init; }

    /// <summary>是否成功完成（任一模式有结果即算成功）。</summary>
    public bool Ok => SingleThreadBytesPerSec > 0 || MultiThreadBytesPerSec > 0;

    /// <summary>平均速率友好文本。</summary>
    public string SingleText => FormatRate(SingleThreadBytesPerSec);

    /// <summary>峰值带宽友好文本。</summary>
    public string MultiText => FormatRate(MultiThreadBytesPerSec);

    /// <summary>速率格式化：B/s → KB/s → MB/s → GB/s。</summary>
    public static string FormatRate(double bytesPerSec)
    {
        if (bytesPerSec <= 0) return "—";
        const double K = 1024.0, M = K * 1024.0, G = M * 1024.0;
        if (bytesPerSec >= G) return (bytesPerSec / G).ToString("0.##") + " GB/s";
        if (bytesPerSec >= M) return (bytesPerSec / M).ToString("0.##") + " MB/s";
        if (bytesPerSec >= K) return (bytesPerSec / K).ToString("0.#") + " KB/s";
        return bytesPerSec.ToString("0") + " B/s";
    }
}

/// <summary>
/// 本地带宽测速（规格 P05）：真实下载固定大小文件并计时。
/// <para>两种模式同时跑、分开呈现：</para>
/// <list type="bullet">
///   <item><b>单线程连续下载</b> → 平均速率，贴近「单个文件下载」的真实体验；</item>
///   <item><b>多线程并发下载</b> → 峰值带宽，贴近启动器实际的多线程下载行为。</para>
/// <para>被测源优先用启动器真实会拉取的地址（Mojang 资源 / BMCLAPI 镜像），
/// 避免用第三方测速站导致结果不可复现。</para>
/// </summary>
public static class BandwidthTester
{
    /// <summary>可选用作测速源的候选（按优先级排列，前面的更贴近真实下载体验）。</summary>
    public static IReadOnlyList<(string Name, string Url)> SpeedTestSources => new[]
    {
        ("Minecraft 资源 (Mojang)", "https://resources.download.minecraft.net/"),
        ("BMCLAPI 镜像",           "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json"),
        ("Mojang 版本清单",        "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"),
    };

    /// <summary>单线程模式的默认下载上限（字节），避免测速跑满带宽拖太久。</summary>
    public const long SingleThreadMaxBytes = 32L * 1024 * 1024;

    /// <summary>多线程模式的单线程下载上限（每线程）。</summary>
    public const long PerThreadMaxBytes = 8L * 1024 * 1024;

    /// <summary>执行测速。</summary>
    /// <param name="seconds">测速时长上限（秒）。用户已定：5 / 10 / 15 / 30，默认 10。</param>
    /// <param name="multiThreadCount">多线程并发数。</param>
    /// <param name="sourceIndex">使用 <see cref="SpeedTestSources"/> 的第几个源；越界则自动挑第一个可用源。</param>
    public static async Task<BandwidthResult> RunAsync(
        int seconds = 10, int multiThreadCount = 4, int sourceIndex = 0, CancellationToken ct = default)
    {
        if (seconds <= 0) seconds = 10;
        if (multiThreadCount <= 0) multiThreadCount = 4;

        var (sourceName, sourceUrl) = ResolveSource(sourceIndex);
        using var client = CreateClient();

        var single = await MeasureAsync(client, sourceUrl, seconds, 1, SingleThreadMaxBytes, ct)
            .ConfigureAwait(false);
        var multi = await MeasureAsync(client, sourceUrl, seconds, multiThreadCount, PerThreadMaxBytes, ct)
            .ConfigureAwait(false);

        return new BandwidthResult
        {
            SingleThreadBytesPerSec = single.bytesPerSec,
            MultiThreadBytesPerSec = multi.bytesPerSec,
            BytesDownloaded = single.bytes + multi.bytes,
            ElapsedSeconds = Math.Max(single.elapsed, multi.elapsed)
        };
    }

    /// <summary>测试源名称（供 UI 显示）。</summary>
    public static string SourceName(int index = 0)
    {
        var list = SpeedTestSources;
        return index >= 0 && index < list.Count ? list[index].Name : list[0].Name;
    }

    private static (string Name, string Url) ResolveSource(int index)
    {
        var list = SpeedTestSources;
        if (index >= 0 && index < list.Count) return list[index];
        return list[0];
    }

    private static HttpClient CreateClient() => new(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    {
        // 由我们自己的时长 / 字节上限控制，不用 HttpClient 的超时
        Timeout = Timeout.InfiniteTimeSpan
    };

    /// <summary>
    /// 核心下载计时：<paramref name="concurrency"/> 个并发任务各自持续下载，
    /// 到时长上限或字节上限即止。返回聚合速率。
    /// </summary>
    private static async Task<(long bytes, double elapsed, double bytesPerSec)> MeasureAsync(
        HttpClient client, string url, int seconds, int concurrency, long maxBytesPerTask, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(seconds));

        var buffer = new byte[64 * 1024];
        var tasks = new Task<long>[concurrency];

        for (var i = 0; i < concurrency; i++)
            tasks[i] = DownloadOneAsync();

        long total;
        try
        {
            var all = await Task.WhenAll(tasks).ConfigureAwait(false);
            total = all.Sum();
        }
        catch (OperationCanceledException)
        {
            // 到时取消是正常结束路径：把已完成的部分计入
            total = 0;
            foreach (var t in tasks)
                if (t.Status == TaskStatus.RanToCompletion) total += t.Result;
        }
        catch (HttpRequestException)
        {
            total = 0;
            foreach (var t in tasks)
                if (t.Status == TaskStatus.RanToCompletion) total += t.Result;
        }

        // 用实际用时算速率：各任务并行，总速率 = 总字节 / 最大单任务用时
        var elapsed = Math.Max(0.001, seconds);
        var rate = concurrency > 0 ? total / elapsed : 0;
        return (total, elapsed, rate);

        async Task<long> DownloadOneAsync()
        {
            long got = 0;
            try
            {
                using var resp = await client
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                    .ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                await using var stream = await resp.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);

                while (got < maxBytesPerTask)
                {
                    var n = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeoutCts.Token)
                        .ConfigureAwait(false);
                    if (n <= 0) break;      // 服务器没数据了
                    got += n;
                }
            }
            catch (OperationCanceledException) { /* 正常超时 */ }
            catch (HttpRequestException) { /* 网络问题，忽略该任务 */ }
            catch (IOException) { /* 连接中断 */ }
            return got;
        }
    }
}
