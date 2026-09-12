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
        ("Minecraft 资源", "https://resources.download.minecraft.net/")
    };

    /// <summary>检测单个端点；超时返回 Reachable=false。</summary>
    public static async Task<DiagnosticResult> ProbeAsync(string name, string url,
        HttpClient? client = null, int timeoutMs = 8000)
    {
        var result = new DiagnosticResult { Name = name, Url = url };
        var own = client is null;
        client ??= new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            sw.Stop();
            result.LatencyMs = sw.ElapsedMilliseconds;
            result.Reachable = resp.IsSuccessStatusCode
                || ((int)resp.StatusCode >= 300 && (int)resp.StatusCode < 500); // 重定向/客户端错误也算可达
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.LatencyMs = sw.ElapsedMilliseconds;
            result.Reachable = false;
            result.Error = ex.GetType().Name;
        }
        finally
        {
            if (own) client.Dispose();
        }
        return result;
    }

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
