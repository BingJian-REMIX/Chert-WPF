using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Chert.Core.Ai;

/// <summary>Ollama 本地服务运行状态。</summary>
public enum OllamaServiceStatus
{
    NotRunning = 0,
    Starting = 1,
    Running = 2
}

/// <summary>本地部署可选模型信息。</summary>
public class LocalModelInfo
{
    /// <summary>显示名，如 Qwen2.5-Coder-1.5B。</summary>
    public string DisplayName { get; set; } = "";
    /// <summary>对应的 Ollama tag，如 qwen2.5-coder:1.5b。</summary>
    public string OllamaTag { get; set; } = "";
    /// <summary>模型体积（GB）。</summary>
    public double SizeGb { get; set; }
    /// <summary>推荐标签，如【默认推荐】。</summary>
    public string RecommendTag { get; set; } = "";
    /// <summary>UI 副文案。</summary>
    public string SubText { get; set; } = "";

    public override string ToString() => DisplayName;
}

/// <summary>本地模型目录（设置 → AI 助手 → 本地部署）。</summary>
public static class OllamaModels
{
    public static IReadOnlyList<LocalModelInfo> Catalog { get; } = new List<LocalModelInfo>
    {
        new()
        {
            DisplayName = "Qwen2.5-Coder-1.5B",
            OllamaTag = "qwen2.5-coder:1.5b",
            SizeGb = 0.9,
            RecommendTag = "【默认推荐】",
            SubText = "中文最优，体积最小，速度最快。覆盖 90% 的崩溃翻译与 Mod 推荐。"
        },
        new()
        {
            DisplayName = "InternLM2-1.8B",
            OllamaTag = "internlm2:1.8b",
            SizeGb = 1.1,
            RecommendTag = "【长日志特化】",
            SubText = "2000 行以上超长崩溃日志分析更强，精准定位 Mod 冲突。"
        },
        new()
        {
            DisplayName = "Phi-3.5-mini-3.8B",
            OllamaTag = "phi3.5:mini",
            SizeGb = 2.2,
            RecommendTag = "【硬核高配】",
            SubText = "复杂 Forge/Fabric 混合堆栈逻辑最强。建议内存 ≥ 16GB。"
        }
    };

    public static LocalModelInfo Default => Catalog[0];

    public static LocalModelInfo? ByDisplayName(string? displayName) =>
        Catalog.FirstOrDefault(m => m.DisplayName == displayName);

    public static LocalModelInfo? ByTag(string? tag) =>
        Catalog.FirstOrDefault(m => string.Equals(m.OllamaTag, tag, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Ollama 本地部署管理器：检测安装、拉取模型、查询服务状态、启动服务。
/// 所有网络/进程异常均被捕获，失败时返回安全默认值，不阻塞主流程。
/// </summary>
public static class OllamaManager
{
    public const string BaseUrl = "http://127.0.0.1:11434";

    /// <summary>本地部署镜像源（registry mirror）。非空时，启动 <c>ollama pull</c> / <c>ollama serve</c> 进程会注入
    /// OLLAMA_REGISTRY 环境变量，使模型权重改从该镜像拉取。留空 = 官方源 registry.ollama.ai。
    /// 由「设置 → AI 助手 → 本地部署 → Ollama 镜像源」写入（默认空）。
    /// 注意：变量末尾不要带斜杠；可用性随 Ollama 版本与镜像站变化。</summary>
    public static string? MirrorRegistryUrl { get; set; }

    /// <summary>安装器地址：Windows 为官方 exe，其它平台为官方安装脚本。</summary>
    public static string InstallerUrl =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "https://ollama.com/download/OllamaSetup.exe"
            : "https://ollama.com/install.sh";

    /// <summary>安装器镜像地址：非空时，<see cref="InstallAsync"/> 优先用该地址下载安装器（加速国内拉取），
    /// 失败自动回退官方与 GitHub 代理。留空 = 官方优先。由「设置 → AI 助手 → 本地部署 → 安装器镜像源」写入（默认空）。
    /// 提示：Windows 可用 GitHub 代理（如 https://ghproxy.net/https://github.com/ollama/ollama/releases/latest/download/OllamaSetup.exe），
    /// 该地址始终重定向到最新版安装器。变量末尾不要带斜杠。</summary>
    public static string? InstallerMirrorUrl { get; set; }

    /// <summary>
    /// 安装器候选地址（按优先级）：用户配置的镜像 → 官方 → GitHub 代理兜底。
    /// issue #5-①：国内直连 ollama.com 常被 TLS 重置，单源下载直接失败；
    /// 改为逐个尝试，任一成功即停。GitHub 代理取 ollama/ollama 的 latest 资产，始终是最新版安装器。
    /// </summary>
    private static IEnumerable<string> InstallerCandidateUrls()
    {
        if (!string.IsNullOrWhiteSpace(InstallerMirrorUrl))
            yield return InstallerMirrorUrl!;
        yield return InstallerUrl;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // 仅 Windows exe 走 GitHub 代理；install.sh 的 GitHub 路径不同，不混用
            const string gh = "https://github.com/ollama/ollama/releases/latest/download/OllamaSetup.exe";
            yield return "https://ghproxy.net/" + gh;
            yield return "https://mirror.ghproxy.com/" + gh;
            yield return "https://ghfast.top/" + gh;
        }
    }

    /// <summary>解析 `ollama --version` 输出中的版本号（如 "ollama version 0.1.2" → "0.1.2"）。</summary>
    public static string? ParseVersion(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        var m = Regex.Match(output, @"(\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.\-]+)?)");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>检测 Ollama 是否已安装及版本号（通过 `ollama --version`）。</summary>
    public static async Task<(bool Installed, string Version)> DetectAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("ollama", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return (false, "");
            var outp = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            var v = ParseVersion(outp);
            return (v is not null, v ?? "");
        }
        catch
        {
            return (false, "");
        }
    }

    /// <summary>查询 Ollama 服务是否可达。</summary>
    public static async Task<OllamaServiceStatus> GetServiceStatusAsync(HttpClient? client = null)
    {
        var own = client is null;
        try
        {
            client ??= new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var resp = await client.GetAsync(BaseUrl + "/api/tags");
            return resp.IsSuccessStatusCode ? OllamaServiceStatus.Running : OllamaServiceStatus.NotRunning;
        }
        catch
        {
            return OllamaServiceStatus.NotRunning;
        }
        finally
        {
            if (own) client?.Dispose();
        }
    }

    /// <summary>判断指定模型是否已拉取（出现在 /api/tags 列表中）。</summary>
    public static async Task<bool> IsModelPulledAsync(string tag, HttpClient? client = null)
    {
        var own = client is null;
        try
        {
            client ??= new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var resp = await client.GetAsync(BaseUrl + "/api/tags");
            if (!resp.IsSuccessStatusCode) return false;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("models", out var arr))
            {
                foreach (var m in arr.EnumerateArray())
                {
                    if (m.TryGetProperty("name", out var n) &&
                        string.Equals(n.GetString(), tag, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (own) client?.Dispose();
        }
    }

    /// <summary>
    /// 安装 Ollama：下载安装器并运行（静默安装），进度回报 0→1。
    /// <para>issue #5-④：未指定版本的安装包（OllamaSetup.exe 等）默认落在<b>启动器同级目录</b>
    /// （<c>AppContext.BaseDirectory</c>），方便用户找到/复用；该目录不可写时回落 %TEMP%。
    /// 安装完成后安装包保留（不删），下次安装可直接复用。</para>
    /// 取消时终止安装进程。仅 Windows 提供 exe 安装器；其它平台下载脚本后由用户手动执行。
    /// </summary>
    public static async Task InstallAsync(IProgress<double>? progress, CancellationToken ct)
    {
        var target = Path.Combine(ResolveDownloadDir(), RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "OllamaSetup.exe" : "install.sh");
        Process? installer = null;
        try
        {
            await DownloadFileAsync(InstallerCandidateUrls(), target, new Progress<double>(p => progress?.Report(p * 0.95)), ct);
            progress?.Report(0.96);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var psi = new ProcessStartInfo(target, "/S")
                {
                    UseShellExecute = true,
                    CreateNoWindow = false
                };
                installer = Process.Start(psi);
            }
            else
            {
                // 非 Windows：赋予执行权限并运行官方安装脚本
                try { Process.Start("chmod", $"+x {target}")?.WaitForExit(); } catch { }
                installer = Process.Start("/bin/sh", target);
            }

            if (installer is null)
                throw new InvalidOperationException("无法启动 Ollama 安装程序");

            using (ct.Register(() =>
            {
                try { installer.Kill(); } catch { }
            }))
            {
                await installer.WaitForExitAsync(ct);
            }
            progress?.Report(1.0);
        }
        finally
        {
            try { installer?.Kill(); } catch { }
            // 安装包保留在启动器同级目录（issue #5-④），不再清理；仅清理未下完的残留
            try
            {
                if (File.Exists(target) && new FileInfo(target).Length == 0) File.Delete(target);
            }
            catch { }
        }
    }

    /// <summary>未指定版本内容的默认落盘目录：启动器同级目录，不可写时回落 %TEMP%。</summary>
    private static string ResolveDownloadDir()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(baseDir) && Directory.Exists(baseDir))
            {
                // 写探针：Program Files 等只读安装位置会抛 UnauthorizedAccessException
                var probe = Path.Combine(baseDir, ".chert_write_probe");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                return baseDir;
            }
        }
        catch { /* 不可写 → 回落临时目录 */ }
        return Path.GetTempPath();
    }

    /// <summary>
    /// 拉取模型（ollama pull）。从 stdout 解析下载进度（completed/total），支持取消（终止进程）。
    /// </summary>
    public static async Task PullModelAsync(string tag, IProgress<double>? progress, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("ollama", $"pull {tag}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (!string.IsNullOrWhiteSpace(MirrorRegistryUrl))
            psi.Environment["OLLAMA_REGISTRY"] = MirrorRegistryUrl!;
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("未找到 ollama，请先安装 Ollama");
        using (ct.Register(() =>
        {
            try { p.Kill(); } catch { }
        }))
        {
            var stdout = p.StandardOutput;
            var buf = new char[1];
            var sb = new StringBuilder();
            // 不要用 stdout.EndOfStream：它在异步方法里会触发一次同步读（CA2024），
            // 既可能阻塞线程，也会和下面的 ReadAsync 抢数据。读到 0 字节就是流结束。
            while (!ct.IsCancellationRequested)
            {
                int read = await stdout.ReadAsync(buf, 0, 1);
                if (read == 0) break;
                sb.Append(buf[0]);
                if (buf[0] == '\n')
                {
                    var line = sb.ToString();
                    sb.Clear();
                    var frac = ParseProgress(line);
                    if (frac.HasValue) progress?.Report(frac.Value);
                }
            }
            if (!p.HasExited) await p.WaitForExitAsync(ct);
        }
        progress?.Report(1.0);
    }

    private static double? ParseProgress(string line)
    {
        // 形如: pulling 7cdf.... [====>                          ]  10 MB/  900 MB
        var m = Regex.Match(line, @"(\d+(?:\.\d+)?)\s*(MB|GB)\s*/\s*(\d+(?:\.\d+)?)\s*(MB|GB)", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var cur = ParseSize(m.Groups[1].Value, m.Groups[2].Value);
        var total = ParseSize(m.Groups[3].Value, m.Groups[4].Value);
        if (total <= 0) return null;
        return Math.Min(1.0, cur / total);
    }

    private static double ParseSize(string num, string unit)
    {
        var v = double.Parse(num, System.Globalization.CultureInfo.InvariantCulture);
        return unit.Equals("GB", StringComparison.OrdinalIgnoreCase) ? v * 1024.0 : v;
    }

    /// <summary>后台启动 Ollama 服务（ollama serve），返回进程；调用方负责轮询状态。</summary>
    public static Process? StartService()
    {
        try
        {
            var psi = new ProcessStartInfo("ollama", "serve")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (!string.IsNullOrWhiteSpace(MirrorRegistryUrl))
                psi.Environment["OLLAMA_REGISTRY"] = MirrorRegistryUrl!;
            return Process.Start(psi);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>下载文件：逐个尝试候选 URL，任一成功即停；全部失败则抛出最后一次异常。</summary>
    private static async Task DownloadFileAsync(IEnumerable<string> urls, string dest, IProgress<double>? progress, CancellationToken ct)
    {
        Exception? last = null;
        foreach (var url in urls)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await DownloadSingleAsync(url, dest, progress, ct);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                last = ex;
                try { if (File.Exists(dest)) File.Delete(dest); } catch { }
            }
        }
        throw last ?? new InvalidOperationException("没有可用的下载地址");
    }

    private static async Task DownloadSingleAsync(string url, string dest, IProgress<double>? progress, CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1L;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(dest);
        var buffer = new byte[8192];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            if (total > 0) progress?.Report(Math.Min(1.0, (double)read / total));
        }
    }
}
