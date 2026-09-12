using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Chert.Core.Launcher;

/// <summary>预热策略。</summary>
public enum PrewarmMode
{
    /// <summary>关闭。</summary>
    Off,
    /// <summary>轻量：只预读版本 jar 与主要库（默认）。</summary>
    Light,
    /// <summary>完整：额外预读资源索引与 natives。</summary>
    Full
}

/// <summary>预热配置。</summary>
public class PrewarmConfig
{
    [JsonPropertyName("mode")]
    public PrewarmMode Mode { get; set; } = PrewarmMode.Light;

    /// <summary>启动器空闲多少秒后开始预热。</summary>
    [JsonPropertyName("idleDelaySec")]
    public int IdleDelaySec
    {
        get => _idleDelaySec;
        set => _idleDelaySec = Math.Clamp(value, 0, 600);
    }
    private int _idleDelaySec = 8;

    /// <summary>单次预热读取的最大字节数（避免占满磁盘缓存）。</summary>
    [JsonPropertyName("budgetMb")]
    public int BudgetMb
    {
        get => _budgetMb;
        set => _budgetMb = Math.Clamp(value, 16, 4096);
    }
    private int _budgetMb = 512;

    /// <summary>并发读取线程数。</summary>
    [JsonPropertyName("concurrency")]
    public int Concurrency
    {
        get => _concurrency;
        set => _concurrency = Math.Clamp(value, 1, 16);
    }
    private int _concurrency = 4;
}

/// <summary>预热计划：待预读的文件清单。</summary>
public class PrewarmPlan
{
    public List<string> Files { get; } = new();
    public long TotalBytes { get; set; }

    /// <summary>因超出预算而被跳过的文件数。</summary>
    public int SkippedByBudget { get; set; }

    public int Count => Files.Count;
    public double TotalMb => Math.Round(TotalBytes / 1024.0 / 1024, 1);
}

/// <summary>预热结果。</summary>
public class PrewarmResult
{
    public bool Ok { get; set; }
    public int FilesRead { get; set; }
    public long BytesRead { get; set; }
    public long ElapsedMs { get; set; }
    public string? Error { get; set; }

    public double MbRead => Math.Round(BytesRead / 1024.0 / 1024, 1);

    /// <summary>吞吐（MB/s），耗时为 0 时返回 0。</summary>
    public double ThroughputMbps => ElapsedMs <= 0 ? 0 : Math.Round(MbRead / (ElapsedMs / 1000.0), 1);
}

/// <summary>
/// 启动预热（全局功能）：在用户还没点"启动"之前，把版本 jar、依赖库等文件预读进系统文件缓存，
/// 缩短首次启动的磁盘等待。纯 I/O 预读，不修改任何文件。
/// </summary>
public static class LaunchPrewarmer
{
    /// <summary>单文件预读的最大字节数（大文件只读前 8MB 足以覆盖热点）。</summary>
    public const int MaxBytesPerFile = 8 * 1024 * 1024;

    /// <summary>
    /// 生成预热计划：按"版本 jar → libraries → natives/assets 索引"的优先级排列，
    /// 累计大小超过预算即停止收集。
    /// </summary>
    public static PrewarmPlan BuildPlan(string gameRoot, string versionId, PrewarmConfig config)
    {
        var plan = new PrewarmPlan();
        if (config.Mode == PrewarmMode.Off) return plan;

        var budget = config.BudgetMb * 1024L * 1024;
        var candidates = new List<string>();

        var versionDir = Path.Combine(gameRoot, "versions", versionId);
        AddIfExists(candidates, Path.Combine(versionDir, versionId + ".jar"));
        AddIfExists(candidates, Path.Combine(versionDir, versionId + ".json"));

        var libDir = Path.Combine(gameRoot, "libraries");
        if (Directory.Exists(libDir))
        {
            try
            {
                candidates.AddRange(Directory
                    .EnumerateFiles(libDir, "*.jar", SearchOption.AllDirectories)
                    .OrderByDescending(f => SafeLength(f)));
            }
            catch
            {
                // 目录不可读：跳过
            }
        }

        if (config.Mode == PrewarmMode.Full)
        {
            var indexes = Path.Combine(gameRoot, "assets", "indexes");
            if (Directory.Exists(indexes))
            {
                try { candidates.AddRange(Directory.EnumerateFiles(indexes, "*.json")); }
                catch { /* ignore */ }
            }

            var natives = Path.Combine(versionDir, "natives");
            if (Directory.Exists(natives))
            {
                try { candidates.AddRange(Directory.EnumerateFiles(natives, "*", SearchOption.AllDirectories)); }
                catch { /* ignore */ }
            }
        }

        foreach (var f in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var len = Math.Min(SafeLength(f), MaxBytesPerFile);
            if (len <= 0) continue;
            if (plan.TotalBytes + len > budget)
            {
                plan.SkippedByBudget++;
                continue;
            }
            plan.Files.Add(f);
            plan.TotalBytes += len;
        }

        return plan;
    }

    /// <summary>执行预热（顺序读取文件头部，把内容带进系统缓存）。</summary>
    public static async Task<PrewarmResult> RunAsync(
        PrewarmPlan plan, PrewarmConfig config,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        var result = new PrewarmResult();
        if (plan.Count == 0) { result.Ok = true; return result; }

        var sw = Stopwatch.StartNew();
        var done = 0;
        long bytes = 0;

        using var gate = new SemaphoreSlim(config.Concurrency);
        try
        {
            var tasks = plan.Files.Select(async file =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var read = await PrereadAsync(file, ct).ConfigureAwait(false);
                    Interlocked.Add(ref bytes, read);
                    var d = Interlocked.Increment(ref done);
                    progress?.Report((d, plan.Count));
                }
                finally
                {
                    gate.Release();
                }
            });
            await Task.WhenAll(tasks).ConfigureAwait(false);

            result.Ok = true;
        }
        catch (OperationCanceledException)
        {
            result.Error = "已取消";
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        sw.Stop();
        result.FilesRead = done;
        result.BytesRead = bytes;
        result.ElapsedMs = sw.ElapsedMilliseconds;
        return result;
    }

    /// <summary>预读单个文件，返回实际读取的字节数。</summary>
    private static async Task<long> PrereadAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 64 * 1024, useAsync: true);

            var buffer = new byte[64 * 1024];
            long total = 0;
            while (total < MaxBytesPerFile)
            {
                var n = await fs.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (n <= 0) break;
                total += n;
            }
            return total;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return 0;   // 文件被占用 / 无权限
        }
    }

    /// <summary>估算预热能省下的启动等待（毫秒）——基于冷读吞吐的经验值。</summary>
    public static long EstimateSavedMs(PrewarmPlan plan, double coldDiskMbps = 80)
    {
        if (plan.Count == 0 || coldDiskMbps <= 0) return 0;
        return (long)(plan.TotalMb / coldDiskMbps * 1000);
    }

    private static void AddIfExists(List<string> list, string path)
    {
        if (File.Exists(path)) list.Add(path);
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }
}
