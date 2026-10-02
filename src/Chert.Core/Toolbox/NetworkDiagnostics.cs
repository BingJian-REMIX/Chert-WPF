using System.Net.Http;

namespace Chert.Core.Toolbox;

/// <summary>单个端点的连通性诊断结果。</summary>
public class DiagnosticResult
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public bool Reachable { get; set; }
    public long LatencyMs { get; set; } = -1;
    public string? Error { get; set; }
}

/// <summary>
/// 网络诊断（工具箱功能 6）：检测 Mojang、Modrinth、BMCLAPI 等服务的
/// 连通性与延迟，用于排查下载/启动失败。
/// </summary>
public static class NetworkDiagnostics
{
    /// <summary>默认待检测的端点。</summary>
    public static IReadOnlyList<(string Name, string Url)> DefaultEndpoints() => new[]
    {
        ("Mojang 官方元数据 (Piston v2)", "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"),
        ("BMCLAPI 镜像 (v2)", "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json"),
        ("Modrinth API", "https://api.modrinth.com/v2/search?limit=1"),
        // P05：CurseForge API —— 整合包 / 模组下载链路是否可达
        ("CurseForge API", "https://api.curseforge.com/v1/games/432"),
        ("Minecraft 资源", "https://resources.download.minecraft.net/")
    };

    /// <summary>检测单个端点；超时返回 Reachable=false。</summary>
    public static async Task<DiagnosticResult> ProbeAsync(string name, string url,
        HttpClient? client = null, int timeoutMs = 8000)
    {
        var result = new DiagnosticResult { Name = name, Url = url };
        var own = client is null;
        client ??= new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };

        // 归一化：缺少 scheme 的纯主机地址（如用户添加的服务器 host）补 http://，
        // 否则 HttpClient 会抛 InvalidOperationException（此前被当成结果展示）。
        var probeUrl = url;
        if (!probeUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !probeUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            probeUrl = "http://" + probeUrl.Trim();
        result.Url = probeUrl;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var resp = await client.GetAsync(probeUrl, HttpCompletionOption.ResponseHeadersRead);
            sw.Stop();
            result.LatencyMs = sw.ElapsedMilliseconds;
            result.Reachable = resp.IsSuccessStatusCode
                || ((int)resp.StatusCode >= 300 && (int)resp.StatusCode < 500); // 重定向/客户端错误也算可达
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.LatencyMs = -1;          // 失败不暴露延迟数值（避免「0 ms」被误读为有效结果）
            result.Reachable = false;
            result.Error = Humanize(ex);    // 人类可读原因，而非异常类名
        }
        finally
        {
            if (own) client.Dispose();
        }
        return result;
    }

    /// <summary>把探测异常翻译成用户可读的原因（不暴露异常类名 / 堆栈）。</summary>
    private static string Humanize(Exception ex) => ex switch
    {
        System.Threading.Tasks.TaskCanceledException or System.TimeoutException => "连接超时",
        HttpRequestException hx when hx.InnerException is System.Net.Sockets.SocketException =>
            "无法连接（网络不可达 / 被拒绝）",
        HttpRequestException hx => $"请求失败：{hx.Message}",
        System.Net.Sockets.SocketException => "无法解析主机或网络不可达",
        InvalidOperationException => "地址无效（缺少 http:// 或 https://）",
        _ => ex.Message
    };

    /// <summary>批量诊断默认端点。</summary>
    public static async Task<List<DiagnosticResult>> DiagnoseAsync(
        IEnumerable<(string Name, string Url)>? endpoints = null, HttpClient? client = null)
    {
        var eps = endpoints?.ToList() ?? DefaultEndpoints().ToList();
        var tasks = eps.Select(e => ProbeAsync(e.Name, e.Url, client)).ToArray();
        await Task.WhenAll(tasks);
        return tasks.Select(t => t.Result).ToList();
    }
}
