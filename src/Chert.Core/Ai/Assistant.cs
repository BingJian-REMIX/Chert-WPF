using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Chert.Core.Ai;

/// <summary>AI 助手运行模式：本地部署（Ollama）或外部 API。</summary>
public enum AiMode { Local, External }

/// <summary>AI 助手配置（设置 → AI 助手）。</summary>
public class AiConfig
{
    public bool Enabled { get; set; }
    /// <summary>部署方式，默认外部 API（零下载、填 Key 即用）。</summary>
    public AiMode Mode { get; set; } = AiMode.External;
    /// <summary>本地部署选中的模型（Ollama tag），默认 Qwen2.5-Coder-1.5B。</summary>
    public string SelectedLocalModel { get; set; } = OllamaModels.Default.OllamaTag;
    public string? ApiKey { get; set; }
    public string Endpoint { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string Model { get; set; } = "gpt-4o-mini";
    public bool CrashInterpret { get; set; } = true;
    public bool RecommendReason { get; set; } = true;
    public bool ModTranslate { get; set; } = true;
}

/// <summary>AI 助手（全局功能 8 / 设置 AI 助手）：本地部署 Ollama 或外部 OpenAI 兼容 API，
/// 用于崩溃解读、推荐理由生成、Mod 描述翻译。所有网络/进程异常都被捕获，失败时回退到本地启发式，
/// 保证不阻塞主流程。</summary>
public static class Assistant
{
    public static AiConfig Config { get; set; } = new();

    /// <summary>根据 API 地址自动推断默认模型名。</summary>
    public static string SuggestModelForEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return "";
        var e = endpoint.ToLowerInvariant();
        if (e.Contains("api.openai.com")) return "gpt-4o-mini";
        if (e.Contains("api.deepseek.com")) return "deepseek-chat";
        if (e.Contains("127.0.0.1") || e.Contains("localhost")) return "";
        return "";
    }

    /// <summary>崩溃解读：将崩溃片段转为人类可读的结论与建议。</summary>
    public static async Task<string> InterpretCrashAsync(string crashText, HttpClient? client = null)
    {
        if (Config.Enabled && Config.CrashInterpret)
        {
            if (Config.Mode == AiMode.External)
            {
                var ext = await CallExternalAsync(
                    "你是 Minecraft 启动器助手。请用中文简要说明下面这段崩溃的原因与修复建议：\n" + crashText, client);
                if (ext is not null) return ext;
            }
            else
            {
                var local = await CallOllamaAsync(
                    "你是 Minecraft 启动器助手。请用中文简要说明下面这段崩溃的原因与修复建议：\n" + crashText, client);
                if (local is not null) return local;
            }
        }
        return LocalCrashInterpret(crashText);
    }

    /// <summary>推荐理由生成。</summary>
    public static async Task<string> GenerateRecommendationReasonAsync(string itemName, string category, string description, HttpClient? client = null)
    {
        if (Config.Enabled && Config.RecommendReason)
        {
            var prompt = $"请用一句中文说明为什么向玩家推荐这个{category}『{itemName}』：{description}";
            if (Config.Mode == AiMode.External)
            {
                var ext = await CallExternalAsync(prompt, client);
                if (ext is not null) return ext;
            }
            else
            {
                var local = await CallOllamaAsync(prompt, client);
                if (local is not null) return local;
            }
        }
        return $"属于{category}，{Truncate(description, 40)}";
    }

    /// <summary>Mod 描述翻译（默认译为中文）。</summary>
    public static async Task<string> TranslateModDescriptionAsync(string text, string targetLang = "中文", HttpClient? client = null)
    {
        if (Config.Enabled && Config.ModTranslate)
        {
            var prompt = $"请将以下文本翻译为{targetLang}：\n{text}";
            if (Config.Mode == AiMode.External)
            {
                var ext = await CallExternalAsync(prompt, client);
                if (ext is not null) return ext;
            }
            else
            {
                var local = await CallOllamaAsync(prompt, client);
                if (local is not null) return local;
            }
        }
        return text;
    }

    /// <summary>通用多轮对话：将用户自由输入发给本地 Ollama 或外部 OpenAI 兼容 API。
    /// 未启用或调用失败时回退到友好提示，保证聊天页始终有反馈。</summary>
    public static async Task<string> ChatAsync(string prompt, HttpClient? client = null)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return "";
        if (Config.Enabled)
        {
            try
            {
                string? reply = Config.Mode == AiMode.External
                    ? await CallExternalAsync(prompt, client)
                    : await CallOllamaAsync(prompt, client);
                if (!string.IsNullOrWhiteSpace(reply)) return reply;
            }
            catch
            {
                // 落到下方回退提示
            }
        }
        return "AI 助手当前未启用或未连接。请在「设置 → AI 助手」中启用外部 API 或本地 Ollama 部署后重试。";
    }

    // ---- 本地启发式（无需模型，最终回退）----

    private static string LocalCrashInterpret(string crash)
    {
        var lower = crash.ToLowerInvariant();
        if (lower.Contains("outofmemoryerror") || lower.Contains("java heap space"))
            return "疑似内存不足（OutOfMemoryError）。建议在「设置 → 启动」中调大最大内存，或关闭占用内存较大的 Mod。";
        if (lower.Contains("unsupportedclassversionerror"))
            return "Java 版本不兼容（UnsupportedClassVersionError）。请安装/切换到 Java 21+ 后重试。";
        if (lower.Contains("classnotfoundexception") || lower.Contains("nosuchmethoderror"))
            return "存在缺失或版本冲突的依赖/Mod。建议检查 Mod 前置是否齐全，或禁用最近新增的 Mod。";
        if (lower.Contains("gl_") || lower.Contains("opengl") || lower.Contains("could not create context"))
            return "显卡/OpenGL 相关错误。建议更新显卡驱动，或在设置中切换渲染相关选项。";
        return "未能自动判定崩溃类型，请附带完整日志到社区或客服进一步排查。";
    }

    private static string Truncate(string s, int n) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s[..n] + "…");

    // ---- 外部 API（OpenAI 兼容）----

    private static async Task<string?> CallExternalAsync(string prompt, HttpClient? client)
    {
        var own = client is null;
        try
        {
            client ??= new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var body = new
            {
                model = Config.Model,
                messages = new[] { new { role = "user", content = prompt } },
                temperature = 0.3
            };
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(Config.ApiKey))
                content.Headers.Add("Authorization", "Bearer " + Config.ApiKey);

            using var resp = await client.PostAsync(Config.Endpoint, content);
            var raw = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var msg = choices[0].GetProperty("message").GetProperty("content").GetString();
                return msg;
            }
            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (own) client?.Dispose();
        }
    }

    // ---- 本地部署（Ollama /api/chat）----

    private static async Task<string?> CallOllamaAsync(string prompt, HttpClient? client)
    {
        var own = client is null;
        try
        {
            // 服务不可达时直接回退，避免无谓等待
            if (await OllamaManager.GetServiceStatusAsync(client) != OllamaServiceStatus.Running)
                return null;
            client ??= new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var tag = string.IsNullOrEmpty(Config.SelectedLocalModel)
                ? OllamaModels.Default.OllamaTag
                : Config.SelectedLocalModel;
            var body = new
            {
                model = tag,
                messages = new[] { new { role = "user", content = prompt } },
                stream = false
            };
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var resp = await client.PostAsync(OllamaManager.BaseUrl + "/api/chat", content);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var c))
                return c.GetString();
            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (own) client?.Dispose();
        }
    }
}
