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
/// 自由输入走 Assistant.ChatAsync（带上下文）；另保留崩溃解读 / Mod 翻译 / 配装推荐 / 年度总结 快捷操作。</summary>
public class AiAssistViewModel : ObservableObject
{
    private const string WelcomeText =
        "你好！我是燧石 AI 助手。可直接输入问题，支持崩溃分析、Mod 推荐、翻译等。";

    /// <summary>自由对话的人设。快捷操作各自有更贴合的 system，不共用这一条。</summary>
    private const string ChatSystemPrompt =
        "你是「燧石启动器」内置的 AI 助手，用中文回答。涉及 Minecraft / 启动器 / Mod / 崩溃的问题请给出可操作步骤，" +
        "不要复述用户的问题；不确定时直说不确定。";

    // 上下文窗口：条数与字数两个闸门，任一超了就从最老的开始丢。
    // 不设上限的话，聊久了每次请求都会把整段历史重发一遍，token 与延迟都线性上涨。
    private const int MaxContextMessages = 20;
    private const int MaxContextChars = 12_000;

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    /// <summary>发给模型的上下文。与界面气泡<b>不是一回事</b>：模型没参与的回答（未启用 / 调用失败 / 本地回退）
    /// 只显示在界面上，不进这里 —— 否则「AI 未启用」会被当成助手自己说过的话，一路污染后续对话。</summary>
    private readonly List<AiChatMessage> _context = new();

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
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetField(ref _isBusy, value)) RefreshCommandStates();
        }
    }

    private string _statusMessage = "";
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    /// <summary>实时读配置：用户在设置页改完 AI 开关回到这一页，不该还是旧状态。</summary>
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
    private readonly ICommand _clearCommand;

    public ICommand SendCommand => _sendCommand;
    public ICommand CrashCommand => _crashCommand;
    public ICommand TranslateCommand => _translateCommand;
    public ICommand RecommendCommand => _recommendCommand;
    public ICommand SummaryCommand => _summaryCommand;
    public ICommand ClearCommand => _clearCommand;

    public AiAssistViewModel()
    {
        _sendCommand = new AsyncRelayCommand(_ => SendAsync(), _ => !IsBusy && !string.IsNullOrWhiteSpace(InputText));
        _crashCommand = new AsyncRelayCommand(_ => CrashAnalyzeAsync(), _ => !IsBusy);
        _translateCommand = new AsyncRelayCommand(_ => TranslateAsync(), _ => !IsBusy);
        _recommendCommand = new AsyncRelayCommand(_ => RecommendAsync(), _ => !IsBusy);
        _summaryCommand = new AsyncRelayCommand(_ => SummaryAsync(), _ => !IsBusy);
        _clearCommand = new RelayCommand(_ => ClearConversation(), _ => !IsBusy);

        Messages.Add(new ChatMessage("assistant", WelcomeText));
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

    // ---- 自由对话（带上下文）----
    private async Task SendAsync()
    {
        var text = InputText?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        InputText = "";

        Messages.Add(new ChatMessage("user", text));
        var context = WithPending(AiChatMessage.User(text));
        IsBusy = true;
        try
        {
            var result = await Assistant.ChatAsync(context, ChatSystemPrompt);
            AppendReply(result, userText: text);
        }
        finally
        {
            IsBusy = false;
        }
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

            var prompt = $"帮我分析上次崩溃（{Path.GetFileName(latest)}）";
            Messages.Add(new ChatMessage("user", prompt));
            var result = await Assistant.InterpretCrashAsync(File.ReadAllText(latest));
            AppendReply(result, userText: prompt);
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessage("assistant", $"分析失败：{ex.Message}"));
        }
        finally
        {
            IsBusy = false;
        }
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

        var prompt = $"请翻译这段 Mod 描述：\n{text}";
        Messages.Add(new ChatMessage("user", prompt));
        InputText = "";
        IsBusy = true;
        try
        {
            var result = await Assistant.TranslateModDescriptionAsync(text);
            AppendReply(result, userText: prompt);
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessage("assistant", $"翻译失败：{ex.Message}"));
        }
        finally
        {
            IsBusy = false;
        }
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

        var prompt = $"帮我推荐适合的 Mod：{pref}";
        Messages.Add(new ChatMessage("user", prompt));
        InputText = "";
        IsBusy = true;
        try
        {
            // 以前这里调的是崩溃解读接口，prompt 会被拼上「说明崩溃原因」—— 现在走专门的推荐接口
            var result = await Assistant.RecommendModsAsync(pref);
            AppendReply(result, userText: prompt);
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessage("assistant", $"推荐失败：{ex.Message}"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- 快捷操作：年度总结 ----
    private async Task SummaryAsync()
    {
        IsBusy = true;
        try
        {
            var prompt = "生成我的年度总结";
            Messages.Add(new ChatMessage("user", prompt));

            var data = AnnualReport.GenerateFrom(LauncherService.Instance.GameRoot, DateTime.Now.Year);
            var md = data.HasData ? AnnualReport.RenderMarkdown(data) : "今年还没有游玩记录。";
            var result = await Assistant.SummarizeAsync(md, "请把这份年度游戏报告总结成一段 100 字以内的话");
            AppendReply(result, userText: prompt);
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessage("assistant", $"生成失败：{ex.Message}"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>清空对话（界面与模型上下文一起清）。</summary>
    private void ClearConversation()
    {
        _context.Clear();
        Messages.Clear();
        Messages.Add(new ChatMessage("assistant", WelcomeText));
        StatusMessage = "已清空对话，模型不再记得前面的内容。";
    }

    // ---- 上下文维护 ----

    /// <summary>把这一轮的新消息接到历史后面（不改动历史本身，失败时可以直接丢弃）。</summary>
    private List<AiChatMessage> WithPending(AiChatMessage pending)
    {
        var list = new List<AiChatMessage>(_context.Count + 1);
        list.AddRange(_context);
        list.Add(pending);
        return list;
    }

    /// <summary>展示回复，并按「模型是否真的参与」决定是否进上下文。</summary>
    private void AppendReply(AiResult result, string? userText = null)
    {
        if (result.FromAi)
        {
            if (!string.IsNullOrEmpty(userText)) _context.Add(AiChatMessage.User(userText!));
            _context.Add(AiChatMessage.Assistant(result.Text));
            TrimContext();
            StatusMessage = "";
        }
        else
        {
            StatusMessage = result.Error ?? "这次模型没参与，下面是本地规则给出的结果。";
        }

        Messages.Add(new ChatMessage("assistant", result.Text));
    }

    private void TrimContext()
    {
        while (_context.Count > MaxContextMessages) _context.RemoveAt(0);

        long total = 0;
        foreach (var m in _context) total += m.Content.Length;
        while (total > MaxContextChars && _context.Count > 1)
        {
            total -= _context[0].Content.Length;
            _context.RemoveAt(0);
        }
    }

    private void RefreshCommandStates()
    {
        OnPropertyChanged(nameof(AiEnabled));
        (_sendCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (_crashCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (_translateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (_recommendCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (_summaryCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (_clearCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}
