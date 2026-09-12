using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chert.Core.Ai;
using Chert.Core.Launcher;
using Chert.Core.Mvvm;
using Chert.Core.Statistics;
using Chert.App.Services;

namespace Chert.App.ViewModels;

/// <summary>聊天消息：role 为 user / assistant。</summary>
public class ChatMessage : ObservableObject
{
    public string Role { get; }
    public string Content { get; }
    public bool IsUser => Role == "user";

    public ChatMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }
}

/// <summary>AI 助手面板（工具箱 aichat）：单页聊天界面，对齐网页 AI 对话（DeepSeek/Kimi）结构。
/// 自由输入走 Assistant.ChatAsync；另保留崩溃解读 / Mod 翻译 / 配装推荐 / 年度总结 快捷操作，避免功能回退。</summary>
public class AiAssistViewModel : ObservableObject
{
    public ObservableCollection<ChatMessage> Messages { get; } = new();

    private string _inputText = "";
    public string InputText
    {
        get => _inputText;
        set
        {
            if (SetField(ref _inputText, value))
                (_sendCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => SetField(ref _isBusy, value); }

    private string _statusMessage = "";
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    public bool AiEnabled => Assistant.Config.Enabled;

    /// <summary>对话区是否仍处于欢迎态（仅有首条问候，未产生任何真实对话）。</summary>
    public bool ShowWelcome => Messages.Count <= 1;

    private ImageSource? _assistantLogo;
    public ImageSource? AssistantLogo
    {
        get => _assistantLogo;
        private set => SetField(ref _assistantLogo, value);
    }

    private bool _hasLogo;
    public bool HasLogo
    {
        get => _hasLogo;
        private set
        {
            if (SetField(ref _hasLogo, value))
                UpdateRobotFallback();
        }
    }

    /// <summary>品牌首字徽章（favicon 失败时兜底）。</summary>
    private string _assistantInitial = "AI";
    public string AssistantInitial
    {
        get => _assistantInitial;
        private set => SetField(ref _assistantInitial, value);
    }

    private Brush _assistantBrandBrush = Brushes.Gray;
    public Brush AssistantBrandBrush
    {
        get => _assistantBrandBrush;
        private set => SetField(ref _assistantBrandBrush, value);
    }

    private bool _hasBrand;
    public bool HasBrand
    {
        get => _hasBrand;
        private set
        {
            if (SetField(ref _hasBrand, value))
                UpdateRobotFallback();
        }
    }

    private bool _showRobotFallback = true;
    public bool ShowRobotFallback
    {
        get => _showRobotFallback;
        private set => SetField(ref _showRobotFallback, value);
    }

    private void UpdateRobotFallback() => ShowRobotFallback = !HasLogo && !HasBrand;

    private readonly ICommand _sendCommand;
    private readonly ICommand _crashCommand;
    private readonly ICommand _translateCommand;
    private readonly ICommand _recommendCommand;
    private readonly ICommand _summaryCommand;

    public ICommand SendCommand => _sendCommand;
    public ICommand CrashCommand => _crashCommand;
    public ICommand TranslateCommand => _translateCommand;
    public ICommand RecommendCommand => _recommendCommand;
    public ICommand SummaryCommand => _summaryCommand;

    public AiAssistViewModel()
    {
        _sendCommand = new AsyncRelayCommand(_ => SendAsync(), _ => !IsBusy && !string.IsNullOrWhiteSpace(InputText));
        _crashCommand = new AsyncRelayCommand(_ => CrashAnalyzeAsync(), _ => !IsBusy);
        _translateCommand = new AsyncRelayCommand(_ => TranslateAsync(), _ => !IsBusy);
        _recommendCommand = new AsyncRelayCommand(_ => RecommendAsync(), _ => !IsBusy);
        _summaryCommand = new AsyncRelayCommand(_ => SummaryAsync(), _ => !IsBusy);

        // 设计稿问候语（首条助手气泡）
        Messages.Add(new ChatMessage("assistant",
            "你好！我是 MCLCS AI 助手。可直接输入问题，支持崩溃分析、Mod 推荐、翻译等。"));
        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowWelcome));

        ResolveBrand();                  // 同步推断品牌：设置首字/底色徽章
        _ = LoadAssistantLogoAsync();    // 异步尝试拉 favicon，成功则覆盖徽章
    }

    // ---- 助手头像：按后端品牌显示首字徽章，并异步拉取官方商标覆盖 ----
    private async Task LoadAssistantLogoAsync()
    {
        // 拉取候选：优先品牌官方图标（国内可直连），失败回退国内 iowen 聚合服务；
        // 仍失败则保留同步算出的品牌首字徽章（HasBrand 兜底）。
        var candidates = new List<string>(2);
        if (!string.IsNullOrEmpty(_brandLogoUrl)) candidates.Add(_brandLogoUrl!);
        if (!string.IsNullOrEmpty(_brandDomain)) candidates.Add($"https://api.iowen.cn/favicon/{_brandDomain}.png");
        if (candidates.Count == 0) return;

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chert");

        foreach (var url in candidates)
        {
            try
            {
                var data = await DownloadWithCacheAsync(client, url);
                if (data is null || data.Length == 0) continue;

                // WPF：从字节流解码 BitmapImage（OnLoad 立即解码，流可释放）
                var bmp = new BitmapImage();
                using (var ms = new MemoryStream(data))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                }
                AssistantLogo = bmp;
                HasLogo = true;
                return; // 任一来源成功即用
            }
            catch
            {
                // 该来源失败，尝试下一个候选
            }
        }
        // 全部失败：保持品牌首字徽章兜底
    }

    private static async Task<byte[]?> DownloadWithCacheAsync(HttpClient client, string url)
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "Chert");
        Directory.CreateDirectory(cacheDir);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16];
        var cacheFile = Path.Combine(cacheDir, "logo_" + key + ".png");
        if (File.Exists(cacheFile)) return await File.ReadAllBytesAsync(cacheFile);
        var data = await client.GetByteArrayAsync(url);
        try { await File.WriteAllBytesAsync(cacheFile, data); } catch { /* 缓存写入失败忽略 */ }
        return data;
    }

    private string? _brandDomain;

    /// <summary>品牌官方图标 URL（尽量 .ico/.png，避开 .svg；为 null 时仅回退 iowen）。</summary>
    private string? _brandLogoUrl;

    /// <summary>同步推断当前部署品牌：设置首字、品牌色、官方图标 URL 与回退域名；未配置则保持机器人兜底。</summary>
    private void ResolveBrand()
    {
        try
        {
            if (Assistant.Config is null || !Assistant.Config.Enabled)
            {
                HasBrand = false;
                return;
            }

            if (Assistant.Config.Mode == AiMode.Local)
            {
                _brandDomain = "ollama.com";
                _brandLogoUrl = "https://ollama.com/favicon.ico";
                AssistantInitial = "O";
                AssistantBrandBrush = new SolidColorBrush(Color.FromRgb(0, 0, 0));
                HasBrand = true;
                return;
            }

            var ep = Assistant.Config.Endpoint ?? "";
            if (string.IsNullOrWhiteSpace(ep))
            {
                HasBrand = false;
                return;
            }

            string host;
            try { host = new Uri(ep).Host; }
            catch { HasBrand = false; return; }
            if (string.IsNullOrWhiteSpace(host))
            {
                HasBrand = false;
                return;
            }

            var h = host.ToLowerInvariant();

            if (h.Contains("openai.com") || h.Contains("api.openai.com"))
            {
                _brandDomain = "openai.com";
                _brandLogoUrl = "https://openai.com/favicon.ico";
                AssistantInitial = "O";
                AssistantBrandBrush = new SolidColorBrush(Color.FromRgb(16, 163, 127));
                HasBrand = true;
                return;
            }
            if (h.Contains("deepseek.com"))
            {
                _brandDomain = "deepseek.com";
                _brandLogoUrl = "https://www.deepseek.com/favicon.ico";
                AssistantInitial = "D";
                AssistantBrandBrush = new SolidColorBrush(Color.FromRgb(76, 154, 255));
                HasBrand = true;
                return;
            }
            if (h.Contains("anthropic.com"))
            {
                _brandDomain = "anthropic.com";
                _brandLogoUrl = "https://claude.ai/images/claude_app_icon.png";
                AssistantInitial = "A";
                AssistantBrandBrush = new SolidColorBrush(Color.FromRgb(207, 90, 85));
                HasBrand = true;
                return;
            }
            if (h.Contains("moonshot.cn"))
            {
                _brandDomain = "moonshot.cn";
                _brandLogoUrl = "https://statics.moonshot.cn/kimi-web-seo/favicon.ico";
                AssistantInitial = "K";
                AssistantBrandBrush = new SolidColorBrush(Color.FromRgb(255, 102, 0));
                HasBrand = true;
                return;
            }
            if (h.Contains("aliyun.com") || h.Contains("dashscope"))
            {
                _brandDomain = "aliyun.com";
                _brandLogoUrl = "https://g.alicdn.com/qwenweb/qwen-ai-fe/0.0.4/favicon.ico";
                AssistantInitial = "通";
                AssistantBrandBrush = new SolidColorBrush(Color.FromRgb(109, 40, 217));
                HasBrand = true;
                return;
            }
            if (h.Contains("mistral.ai"))
            {
                _brandDomain = "mistral.ai";
                _brandLogoUrl = "https://mistral.ai/favicon.ico";
                AssistantInitial = "M";
                AssistantBrandBrush = new SolidColorBrush(Color.FromRgb(255, 0, 106));
                HasBrand = true;
                return;
            }
            if (h.Contains("groq.com"))
            {
                _brandDomain = "groq.com";
                _brandLogoUrl = "https://groq.com/favicon.ico";
                AssistantInitial = "G";
                AssistantBrandBrush = new SolidColorBrush(Color.FromRgb(250, 0, 80));
                HasBrand = true;
                return;
            }
            if (h.Contains("googleapis.com"))
            {
                _brandDomain = "google.com";
                _brandLogoUrl = null; // Gemini 官方图标在海外 gstatic，国内不稳，仅走 iowen 回退
                AssistantInitial = "G";
                AssistantBrandBrush = new SolidColorBrush(Color.FromRgb(66, 133, 244));
                HasBrand = true;
                return;
            }

            _brandDomain = GetRegistrableDomain(host);
            _brandLogoUrl = null; // 未知品牌：仅走 iowen 回退
            AssistantInitial = char.ToUpperInvariant(host[0]).ToString();
            AssistantBrandBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246));
            HasBrand = true;
        }
        catch
        {
            HasBrand = false;
        }
    }

    /// <summary>简化版注册域名提取（无额外依赖；未知品牌取二级域名，常见二级公共后缀单独处理）。</summary>
    private static string GetRegistrableDomain(string host)
    {
        var parts = host.Split('.');
        if (parts.Length <= 2) return host;
        var lastTwo = parts[^2] + "." + parts[^1];
        var twoLevelTlds = new[] { "co.uk", "com.cn", "org.cn", "net.cn", "com.au", "co.jp" };
        return Array.Exists(twoLevelTlds, t => t == lastTwo)
            ? parts[^3] + "." + lastTwo
            : lastTwo;
    }

    // ---- 自由对话 ----
    private async Task SendAsync()
    {
        var text = InputText?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        InputText = "";
        Messages.Add(new ChatMessage("user", text));
        IsBusy = true;
        try
        {
            var reply = await Assistant.ChatAsync(text);
            Messages.Add(new ChatMessage("assistant", reply));
        }
        finally { IsBusy = false; }
    }

    // ---- 快捷操作：崩溃分析 ----
    private async Task CrashAnalyzeAsync()
    {
        IsBusy = true;
        try
        {
            var root = LauncherService.Instance.GameRoot;
            var latest = CrashDetector.FindLatestCrashReport(root);
            if (latest is null)
            {
                Messages.Add(new ChatMessage("user", "帮我分析上次崩溃"));
                Messages.Add(new ChatMessage("assistant",
                    "未找到崩溃报告文件（crash-reports 目录为空）。如有日志，可直接粘贴到下方输入框，我会帮你分析。"));
                return;
            }
            Messages.Add(new ChatMessage("user", $"帮我分析上次崩溃（{Path.GetFileName(latest)}）"));
            var result = await Assistant.InterpretCrashAsync(File.ReadAllText(latest));
            Messages.Add(new ChatMessage("assistant", result));
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessage("assistant", $"分析失败：{ex.Message}"));
        }
        finally { IsBusy = false; }
    }

    // ---- 快捷操作：Mod 描述翻译 ----
    private async Task TranslateAsync()
    {
        var text = InputText?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            StatusMessage = "请在输入框粘贴 Mod 描述后点击「Mod 翻译」";
            return;
        }
        Messages.Add(new ChatMessage("user", $"请翻译这段 Mod 描述：\n{text}"));
        InputText = "";
        IsBusy = true;
        try
        {
            var r = await Assistant.TranslateModDescriptionAsync(text);
            Messages.Add(new ChatMessage("assistant", r));
        }
        finally { IsBusy = false; }
    }

    // ---- 快捷操作：配装推荐 ----
    private async Task RecommendAsync()
    {
        var pref = InputText?.Trim();
        if (string.IsNullOrEmpty(pref))
        {
            StatusMessage = "请在输入框描述你的玩法偏好后点击「配装推荐」";
            return;
        }
        Messages.Add(new ChatMessage("user", $"帮我推荐适合的 Mod：{pref}"));
        InputText = "";
        IsBusy = true;
        try
        {
            var r = Assistant.Config.Enabled
                ? await Assistant.InterpretCrashAsync($"请根据以下偏好推荐5个Minecraft Mod（仅列名称和简要理由）：{pref}")
                : "AI 未启用，请在「设置 → AI 助手」中开启后使用此功能。";
            Messages.Add(new ChatMessage("assistant", r));
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessage("assistant", $"推荐失败：{ex.Message}"));
        }
        finally { IsBusy = false; }
    }

    // ---- 快捷操作：年度总结 ----
    private async Task SummaryAsync()
    {
        Messages.Add(new ChatMessage("user", "生成我的年度总结"));
        IsBusy = true;
        try
        {
            if (!AiEnabled)
            {
                Messages.Add(new ChatMessage("assistant", "AI 未启用，请在「设置 → AI 助手」中开启后使用此功能。"));
                return;
            }
            var data = AnnualReport.GenerateFrom(LauncherService.Instance.GameRoot, DateTime.Now.Year);
            var md = data.HasData ? AnnualReport.RenderMarkdown(data) : "今年还没有游玩记录。";
            var r = await Assistant.InterpretCrashAsync($"请将以下年度游戏报告总结成一段100字以内的话：\n{md}");
            Messages.Add(new ChatMessage("assistant", r));
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessage("assistant", $"生成失败：{ex.Message}"));
        }
        finally { IsBusy = false; }
    }
}
