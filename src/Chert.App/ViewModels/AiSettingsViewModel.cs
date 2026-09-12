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
    public IReadOnlyList<LocalModelInfo> LocalModels => OllamaModels.Catalog;

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

    public string SelectedModelSubText => OllamaModels.ByDisplayName(SelectedLocalModel)?.SubText ?? "";
    public string SelectedModelSizeText =>
        OllamaModels.ByDisplayName(SelectedLocalModel) is { } m ? $"{m.SizeGb} GB · {m.RecommendTag}" : "";
    public string ModelButtonText =>
        ModelReady ? "已就绪"
        : (OllamaModels.ByDisplayName(SelectedLocalModel) is { } m ? $"下载模型 ({m.SizeGb}GB)" : "下载模型");

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

    public AiSettingsViewModel()
    {
        InstallOllamaCommand = new AsyncRelayCommand(_ => InstallOllamaAsync());
        CancelOllamaInstallCommand = new RelayCommand(_ => _ollamaInstallCts?.Cancel());
        PullModelCommand = new AsyncRelayCommand(_ => PullModelAsync());
        CancelModelDownloadCommand = new RelayCommand(_ => _modelCts?.Cancel());
        TestConnectionCommand = new AsyncRelayCommand(_ => TestConnectionAsync());
        RefreshAiStatusCommand = new AsyncRelayCommand(_ => RefreshStatusAsync());
    }

    /// <summary>从 profile 填充 AI 配置（对齐 Linux 构造逻辑）。</summary>
    public void Load(LauncherProfile profile)
    {
        var ai = profile.Ai ?? new AiConfig();
        AiEnabled = ai.Enabled;
        AiMode = ai.Mode.ToString();
        SelectedLocalModel = OllamaModels.ByTag(ai.SelectedLocalModel)?.DisplayName ?? OllamaModels.Default.DisplayName;
        _lastCommittedModel = SelectedLocalModel;
        AiApiKey = ai.ApiKey ?? "";
        AiEndpoint = ai.Endpoint;
        AiModel = ai.Model;
        AiCrashInterpret = ai.CrashInterpret;
        AiRecommendReason = ai.RecommendReason;
        AiModTranslate = ai.ModTranslate;
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
            SelectedLocalModel = OllamaModels.ByDisplayName(SelectedLocalModel)?.OllamaTag ?? OllamaModels.Default.OllamaTag,
            ApiKey = string.IsNullOrWhiteSpace(AiApiKey) ? null : AiApiKey,
            Endpoint = AiEndpoint,
            Model = AiModel,
            CrashInterpret = AiCrashInterpret,
            RecommendReason = AiRecommendReason,
            ModTranslate = AiModTranslate
        };
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

            _pulledTags.Clear();
            foreach (var m in OllamaModels.Catalog)
                if (await OllamaManager.IsModelPulledAsync(m.OllamaTag))
                    _pulledTags.Add(m.OllamaTag);

            var info = OllamaModels.ByDisplayName(SelectedLocalModel);
            ModelReady = info is not null && _pulledTags.Contains(info.OllamaTag);
        }
        catch
        {
            // 离线/无 Ollama 时不阻塞界面
        }
    }

    private void RefreshModelReady()
    {
        var info = OllamaModels.ByDisplayName(SelectedLocalModel);
        ModelReady = info is not null && _pulledTags.Contains(info.OllamaTag);
        OnPropertyChanged(nameof(ModelButtonText));
    }

    /// <summary>用户切换本地模型时的回调：已拉取直接接受；未拉取则弹确认窗，取消则回退。</summary>
    public async Task TrySelectLocalModelAsync(string displayName)
    {
        var info = OllamaModels.ByDisplayName(displayName);
        if (info is null) return;
        if (displayName == _lastCommittedModel || _pulledTags.Contains(info.OllamaTag))
        {
            _lastCommittedModel = displayName;
            return;
        }

        var msg = info.OllamaTag.Contains("phi")
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
        var info = OllamaModels.ByDisplayName(SelectedLocalModel);
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

    /// <summary>测试外部 API 连接，成功后按地址自动补全默认模型名。</summary>
    private async Task TestConnectionAsync()
    {
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
}
