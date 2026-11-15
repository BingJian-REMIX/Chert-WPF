using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Chert.Core.Ai;
using Chert.Core.Mvvm;
using Chert.Core.Profiles;
using Chert.App.Services;

namespace Chert.App.ViewModels;

/// <summary>
/// 设置 → AI 助手（对齐 Linux AiSettingsView / AiSettingsViewModel）：总开关 + 部署方式 +
/// 本地部署面板（Ollama 安装 / 进度 / 模型目录 / 拉取 / 服务灯）+ 外部 API 面板（含测试连接）+
/// 三项能力开关。配置持久化到 LauncherProfile.Ai，保存时由 SettingsViewModel 调用 ApplyTo 写回。
/// </summary>
public class AiSettingsViewModel : ObservableObject
{
    private readonly HashSet<string> _pulledTags = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<LocalModelInfo> _localModels = OllamaModels.Catalog;
    private string _customLocalModelTag = "";

    // ---- 配置（绑定到 profile.Ai 的副本，ApplyTo 时回写）----
    private bool _aiEnabled;
    private string _aiMode = "External";
    private string _aiEndpoint = "https://api.openai.com/v1/chat/completions";
    private string _aiModel = "gpt-4o-mini";
    private string? _aiApiKey;
    private string _selectedLocalModel = OllamaModels.Default.DisplayName;
    private string _lastCommittedModel = OllamaModels.Default.DisplayName;
    private bool _aiCrashInterpret = true;
    private bool _aiRecommendReason = true;
    private bool _aiModTranslate = true;

    // ---- 服务状态（只读探测）----
    private string _status = "";
    private bool _ollamaInstalled;
    private string _ollamaVersion = "";
    private bool _ollamaInstalling;
    private double _ollamaInstallProgress;
    private string _ollamaInstallText = "";
    private bool _modelDownloading;
    private double _modelDownloadProgress;
    private string _modelDownloadText = "";
    private bool _modelReady;
    private OllamaServiceStatus _ollamaServiceStatus = OllamaServiceStatus.NotRunning;
    private CancellationTokenSource? _ollamaInstallCts;
    private CancellationTokenSource? _modelCts;

    // ---- 配置 ----
    public bool AiEnabled
    {
        get => _aiEnabled;
        set
        {
            if (SetField(ref _aiEnabled, value))
            {
                OnPropertyChanged(nameof(AiDetailsVisibility));
                OnPropertyChanged(nameof(LocalDeployVisibility));
                OnPropertyChanged(nameof(ExternalDeployVisibility));
            }
        }
    }

    public string AiMode
    {
        get => _aiMode;
        set
        {
            if (SetField(ref _aiMode, value))
            {
                OnPropertyChanged(nameof(LocalDeployVisibility));
                OnPropertyChanged(nameof(ExternalDeployVisibility));
            }
        }
    }

    public string AiApiKey { get => _aiApiKey ?? ""; set => SetField(ref _aiApiKey, value); }
    public string AiEndpoint { get => _aiEndpoint; set => SetField(ref _aiEndpoint, value); }
    public string AiModel { get => _aiModel; set => SetField(ref _aiModel, value); }
    public bool AiCrashInterpret { get => _aiCrashInterpret; set => SetField(ref _aiCrashInterpret, value); }
    public bool AiRecommendReason { get => _aiRecommendReason; set => SetField(ref _aiRecommendReason, value); }
    public bool AiModTranslate { get => _aiModTranslate; set => SetField(ref _aiModTranslate, value); }

    // ---- 本地部署（Ollama）----
    /// <summary>下拉项 = 本机已装（/api/tags 动态发现）+ 预设可下载模型 + 用户自定义 tag。</summary>
    public IReadOnlyList<LocalModelInfo> LocalModels => _localModels;

    public string CustomLocalModelTag
    {
        get => _customLocalModelTag;
        set => SetField(ref _customLocalModelTag, value);
    }

    /// <summary>解析当前选中项：命中列表用其 tag；未命中则把输入文本本身当自定义 tag。</summary>
    private LocalModelInfo? ResolveSelected()
    {
        var name = SelectedLocalModel;
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var m in _localModels)
            if (string.Equals(m.DisplayName, name, StringComparison.OrdinalIgnoreCase)) return m;
        return MakeCustomItem(name.Trim(), _pulledTags.Contains(name.Trim()));
    }

    private static LocalModelInfo MakeCustomItem(string tag, bool installed) => new()
    {
        DisplayName = tag,
        OllamaTag = tag,
        RecommendTag = installed ? "【本机已装】" : "【自定义】",
        SubText = installed
            ? "从本机 Ollama 检测到的模型，可直接用于本地部署。"
            : "自定义 Ollama tag；若尚未拉取，可点「下载模型」拉取。"
    };

    public string SelectedLocalModel
    {
        get => _selectedLocalModel;
        set
        {
            if (SetField(ref _selectedLocalModel, value))
            {
                OnPropertyChanged(nameof(SelectedModelSubText));
                OnPropertyChanged(nameof(SelectedModelSizeText));
                OnPropertyChanged(nameof(ModelButtonText));
                RefreshModelReady();
            }
        }
    }

    public string SelectedModelSubText => ResolveSelected()?.SubText ?? "";
    public string SelectedModelSizeText =>
        ResolveSelected() is { } m
            ? (m.SizeGb > 0 ? $"{m.SizeGb} GB · {m.RecommendTag}" : m.RecommendTag)
            : "";
    public string ModelButtonText =>
        ModelReady ? "已就绪"
        : (ResolveSelected() is { } m && m.SizeGb > 0 ? $"下载模型 ({m.SizeGb}GB)" : "下载模型");

    public bool OllamaInstalled { get => _ollamaInstalled; set => SetField(ref _ollamaInstalled, value); }
    public string OllamaVersion { get => _ollamaVersion; set => SetField(ref _ollamaVersion, value); }
    public bool OllamaInstalling { get => _ollamaInstalling; set => SetField(ref _ollamaInstalling, value); }
    public double OllamaInstallProgress { get => _ollamaInstallProgress; set => SetField(ref _ollamaInstallProgress, value); }
    public string OllamaInstallText { get => _ollamaInstallText; set => SetField(ref _ollamaInstallText, value); }
    public bool ModelDownloading { get => _modelDownloading; set => SetField(ref _modelDownloading, value); }
    public double ModelDownloadProgress { get => _modelDownloadProgress; set => SetField(ref _modelDownloadProgress, value); }
    public string ModelDownloadText { get => _modelDownloadText; set => SetField(ref _modelDownloadText, value); }
    public bool ModelReady { get => _modelReady; set => SetField(ref _modelReady, value); }
    public OllamaServiceStatus OllamaServiceStatus { get => _ollamaServiceStatus; set => SetField(ref _ollamaServiceStatus, value); }

    private string _ollamaMirrorUrl = "";
    /// <summary>Ollama 镜像源（registry mirror），对应 ollama 的 OLLAMA_REGISTRY 环境变量。留空 = 官方源。</summary>
    public string OllamaMirrorUrl
    {
        get => _ollamaMirrorUrl;
        set { if (SetField(ref _ollamaMirrorUrl, value)) OllamaManager.MirrorRegistryUrl = value; }
    }

    private string _ollamaInstallerMirrorUrl = "";
    /// <summary>Ollama 安装器镜像源：非空时安装流程改用该地址下载安装器（加速国内拉取）。留空 = 官方安装器。</summary>
    public string OllamaInstallerMirrorUrl
    {
        get => _ollamaInstallerMirrorUrl;
        set { if (SetField(ref _ollamaInstallerMirrorUrl, value)) OllamaManager.InstallerMirrorUrl = value; }
    }

    // 分层可见性（依总开关与部署方式）
    public Visibility AiDetailsVisibility => AiEnabled ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LocalDeployVisibility => (AiEnabled && AiMode == "Local") ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ExternalDeployVisibility => (AiEnabled && AiMode == "External") ? Visibility.Visible : Visibility.Collapsed;

    // 服务状态文本（对齐 Linux Status）
    public string Status { get => _status; set => SetField(ref _status, value); }

    // ---- 命令 ----
    public ICommand InstallOllamaCommand { get; }
    public ICommand CancelOllamaInstallCommand { get; }
    public ICommand PullModelCommand { get; }
    public ICommand CancelModelDownloadCommand { get; }
    public ICommand TestConnectionCommand { get; }
    public ICommand RefreshAiStatusCommand { get; }
    public ICommand AddCustomModelCommand { get; }

    public AiSettingsViewModel()
    {
        InstallOllamaCommand = new AsyncRelayCommand(_ => InstallOllamaAsync());
        CancelOllamaInstallCommand = new RelayCommand(_ => _ollamaInstallCts?.Cancel());
        PullModelCommand = new AsyncRelayCommand(_ => PullModelAsync());
        CancelModelDownloadCommand = new RelayCommand(_ => _modelCts?.Cancel());
        TestConnectionCommand = new AsyncRelayCommand(_ => TestConnectionAsync());
        RefreshAiStatusCommand = new AsyncRelayCommand(_ => RefreshStatusAsync());
        AddCustomModelCommand = new RelayCommand(_ => AddCustomModel());
    }

    /// <summary>从 profile 填充 AI 配置（对齐 Linux 构造逻辑）。</summary>
    public void Load(LauncherProfile profile)
    {
        var ai = profile.Ai ?? new AiConfig();
        AiEnabled = ai.Enabled;
        AiMode = ai.Mode.ToString();
        // 自定义 tag 可能不在预设目录里，先补进下拉项，避免绑定初始化把选中值清空
        var localTag = ai.SelectedLocalModel;
        var preset = OllamaModels.ByTag(localTag);
        if (preset is null && !string.IsNullOrWhiteSpace(localTag))
            EnsureLocalModelItem(localTag!);
        SelectedLocalModel = preset?.DisplayName
            ?? (string.IsNullOrWhiteSpace(localTag) ? OllamaModels.Default.DisplayName : localTag!);
        _lastCommittedModel = SelectedLocalModel;
        // 落盘的是混淆串（老配置可能是明文，RevealApiKey 会兼容），输入框要的是明文
        AiApiKey = Assistant.RevealApiKey(ai);
        AiEndpoint = ai.Endpoint;
        AiModel = ai.Model;
        AiCrashInterpret = ai.CrashInterpret;
        AiRecommendReason = ai.RecommendReason;
        AiModTranslate = ai.ModTranslate;
        OllamaMirrorUrl = ai.OllamaMirrorUrl ?? "";
        OllamaInstallerMirrorUrl = ai.OllamaInstallerMirrorUrl ?? "";
    }

    /// <summary>写回 AI 配置到 profile（对齐 Linux Save）。</summary>
    public void ApplyTo(LauncherProfile profile)
    {
        profile.Ai = new AiConfig
        {
            Enabled = AiEnabled,
            // 本类有同名 string 属性 AiMode，会遮蔽枚举类型，故用完全限定名。
            Mode = Enum.TryParse<Chert.Core.Ai.AiMode>(AiMode, out var am)
                ? am
                : Chert.Core.Ai.AiMode.External,
            SelectedLocalModel = ResolveSelected()?.OllamaTag ?? OllamaModels.Default.OllamaTag,
            ApiKey = null,   // 真正的取值在下面 CommitApiKey 里混淆后写入
            Endpoint = AiEndpoint,
            Model = AiModel,
            CrashInterpret = AiCrashInterpret,
            RecommendReason = AiRecommendReason,
            ModTranslate = AiModTranslate,
            OllamaMirrorUrl = OllamaMirrorUrl,
            OllamaInstallerMirrorUrl = OllamaInstallerMirrorUrl
        };

        // Key 不进明文：内存留一份供本次会话直接用，落盘那一份混淆
        Assistant.CommitApiKey(string.IsNullOrWhiteSpace(AiApiKey) ? null : AiApiKey, profile.Ai);
    }

    /// <summary>检测 Ollama 安装、服务状态与已拉取模型（后台刷新，失败不阻塞界面）。</summary>
    public async Task RefreshStatusAsync()
    {
        try
        {
            var det = await OllamaManager.DetectAsync();
            OllamaInstalled = det.Installed;
            OllamaVersion = det.Version;
            OllamaServiceStatus = await OllamaManager.GetServiceStatusAsync();

            var installedTags = await OllamaManager.ListInstalledTagsAsync();
            _pulledTags.Clear();
            foreach (var tag in installedTags)
            {
                _pulledTags.Add(tag);
                // Ollama 接受省略 :latest 的写法，两种 tag 都算已装
                if (tag.EndsWith(":latest", StringComparison.OrdinalIgnoreCase))
                    _pulledTags.Add(tag.Substring(0, tag.Length - ":latest".Length));
            }
            RebuildLocalModels(installedTags);
            RefreshModelReady();
        }
        catch
        {
            // 离线/无 Ollama 时不阻塞界面
        }
    }

    private void RefreshModelReady()
    {
        var info = ResolveSelected();
        ModelReady = info is not null && _pulledTags.Contains(info.OllamaTag);
        OnPropertyChanged(nameof(ModelButtonText));
    }

    /// <summary>按 /api/tags 重建下拉项：已装的非预设模型优先，随后是预设目录；保留当前自定义选择。</summary>
    private void RebuildLocalModels(IReadOnlyList<string> installedTags)
    {
        var list = new List<LocalModelInfo>();
        foreach (var tag in installedTags)
            if (OllamaModels.ByTag(tag) is null &&
                !list.Any(m => string.Equals(m.OllamaTag, tag, StringComparison.OrdinalIgnoreCase)))
                list.Add(MakeCustomItem(tag, true));
        list.AddRange(OllamaModels.Catalog);
        var current = SelectedLocalModel;
        if (!string.IsNullOrWhiteSpace(current) &&
            !list.Any(m => string.Equals(m.DisplayName, current, StringComparison.OrdinalIgnoreCase)))
            list.Insert(0, MakeCustomItem(current.Trim(), _pulledTags.Contains(current.Trim())));
        _localModels = list;
        OnPropertyChanged(nameof(LocalModels));
    }

    /// <summary>把一个自定义 tag 补进下拉项（幂等）。</summary>
    private void EnsureLocalModelItem(string tag)
    {
        var t = tag.Trim();
        if (_localModels.Any(m => string.Equals(m.OllamaTag, t, StringComparison.OrdinalIgnoreCase))) return;
        var list = _localModels.ToList();
        list.Insert(0, MakeCustomItem(t, _pulledTags.Contains(t)));
        _localModels = list;
        OnPropertyChanged(nameof(LocalModels));
    }

    /// <summary>用户手动输入 Ollama tag 后加入下拉并选中，落盘时写入该 tag。</summary>
    private void AddCustomModel()
    {
        var tag = CustomLocalModelTag?.Trim();
        if (string.IsNullOrEmpty(tag))
        {
            Status = "请先填写模型 tag";
            return;
        }
        EnsureLocalModelItem(tag);
        SelectedLocalModel = tag;
        _lastCommittedModel = tag;
        CustomLocalModelTag = "";
        Status = $"已添加模型 tag：{tag}";
    }

    /// <summary>用户切换本地模型时的回调：已拉取直接接受；未拉取则弹确认窗，取消则回退。</summary>
    public async Task TrySelectLocalModelAsync(string displayName)
    {
        var info = _localModels.FirstOrDefault(m => m.DisplayName == displayName);
        if (info is null) return;
        if (displayName == _lastCommittedModel || _pulledTags.Contains(info.OllamaTag))
        {
            _lastCommittedModel = displayName;
            return;
        }

        var msg = info.SizeGb <= 0
            ? "该 tag 尚未在本机拉取，是否现在下载？"
            : info.OllamaTag.Contains("phi")
                ? "需额外下载 2.2GB，文件较大，是否继续？"
                : info.OllamaTag.Contains("internlm")
                    ? "需额外下载 1.1GB，是否继续？"
                    : $"需额外下载 {info.SizeGb}GB，是否继续？";

        if (!UIService.Confirm(msg, "下载模型"))
        {
            // 回退到上一次已确认的模型
            SelectedLocalModel = _lastCommittedModel;
            return;
        }
        _lastCommittedModel = displayName;
    }

    /// <summary>一键安装 Ollama：下载安装器并静默安装，支持取消与临时文件清理。</summary>
    private async Task InstallOllamaAsync()
    {
        _ollamaInstallCts = new CancellationTokenSource();
        OllamaInstalling = true;
        OllamaInstallProgress = 0;
        OllamaInstallText = "正在下载 Ollama 安装程序…";
        try
        {
            await OllamaManager.InstallAsync(new Progress<double>(p => OllamaInstallProgress = p), _ollamaInstallCts.Token);
            var det = await OllamaManager.DetectAsync();
            OllamaInstalled = det.Installed;
            OllamaVersion = det.Version;
            OllamaInstallText = det.Installed
                ? $"Ollama 已安装（{det.Version}）"
                : "安装完成，但未检测到 ollama 命令，请重启启动器后重试。";
            await RefreshStatusAsync();
        }
        catch (OperationCanceledException)
        {
            OllamaInstallText = "已取消安装，临时文件已清理。";
        }
        catch (Exception ex)
        {
            OllamaInstallText = $"安装失败：{ex.Message}";
        }
        finally
        {
            OllamaInstalling = false;
            _ollamaInstallCts = null;
        }
    }

    /// <summary>拉取选中的本地模型，支持进度与取消。</summary>
    private async Task PullModelAsync()
    {
        var info = ResolveSelected();
        if (info is null) return;
        _modelCts = new CancellationTokenSource();
        ModelDownloading = true;
        ModelDownloadProgress = 0;
        ModelDownloadText = $"正在下载 {info.DisplayName}…";
        try
        {
            await OllamaManager.PullModelAsync(info.OllamaTag,
                new Progress<double>(p => ModelDownloadProgress = p), _modelCts.Token);
            _pulledTags.Add(info.OllamaTag);
            _lastCommittedModel = SelectedLocalModel;
            ModelReady = true;
            ModelDownloadText = "已就绪";
        }
        catch (OperationCanceledException)
        {
            ModelDownloadText = "已取消下载。";
        }
        catch (Exception ex)
        {
            ModelDownloadText = $"下载失败：{ex.Message}";
        }
        finally
        {
            ModelDownloading = false;
            _modelCts = null;
            RefreshModelReady();
        }
    }

    /// <summary>测试连接：本地模式打 /api/chat，外部模式打用户填写的 OpenAI 兼容端点。</summary>
    private async Task TestConnectionAsync()
    {
        if (AiMode == "Local")
        {
            await TestLocalConnectionAsync();
            return;
        }
        if (string.IsNullOrWhiteSpace(AiEndpoint))
        {
            Status = "请先填写 API 地址";
            return;
        }
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var body = new
            {
                model = string.IsNullOrWhiteSpace(AiModel) ? "gpt-4o-mini" : AiModel,
                messages = new[] { new { role = "user", content = "ping" } },
                max_tokens = 1
            };
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            if (!string.IsNullOrWhiteSpace(AiApiKey))
                content.Headers.Add("Authorization", "Bearer " + AiApiKey);

            using var resp = await client.PostAsync(AiEndpoint, content);
            if (resp.IsSuccessStatusCode)
            {
                var suggested = Assistant.SuggestModelForEndpoint(AiEndpoint);
                if (!string.IsNullOrEmpty(suggested) && suggested != AiModel)
                {
                    AiModel = suggested;
                    Status = $"连接成功，已自动填充模型：{suggested}";
                }
                else
                {
                    Status = "连接成功";
                }
            }
            else
            {
                Status = $"连接失败：HTTP {(int)resp.StatusCode}";
            }
        }
        catch (Exception ex)
        {
            Status = $"连接失败：{ex.Message}";
        }
    }

    /// <summary>本地模式实测：用当前选中的 tag 发一次 /api/chat，冷启动最多等 90 秒。</summary>
    private async Task TestLocalConnectionAsync()
    {
        var tag = ResolveSelected()?.OllamaTag;
        if (string.IsNullOrWhiteSpace(tag))
        {
            Status = "请先选择或添加本地模型";
            return;
        }
        Status = $"正在测试本地模型 {tag}（首次加载可能较慢）…";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            var body = new
            {
                model = tag,
                messages = new[] { new { role = "user", content = "ping" } },
                stream = false,
                options = new { num_predict = 1 }
            };
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var resp = await client.PostAsync(OllamaManager.BaseUrl + "/api/chat", content);
            if (resp.IsSuccessStatusCode)
            {
                Status = $"本地连接成功：{tag}";
            }
            else
            {
                var detail = await resp.Content.ReadAsStringAsync();
                if (detail.Length > 160) detail = detail.Substring(0, 160);
                Status = $"本地连接失败：HTTP {(int)resp.StatusCode} {detail}";
            }
        }
        catch (Exception ex)
        {
            Status = $"本地连接失败：{ex.Message}";
        }
    }
}
