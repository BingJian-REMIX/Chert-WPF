using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Chert.Core.Music;
using Chert.Core.Toolbox;
using Chert.Core.Mvvm;
using Chert.Core.Profiles;
using Chert.Core.Utils;
using Chert.App.Services;

namespace Chert.App.ViewModels;

/// <summary>实际音频解码宿主（由界面层用 MediaElement 实现并注入）。</summary>
public interface IMediaPlayer
{
    void LoadAndPlay(string path);
    void Pause();
    void Resume();
    void Stop();
    void SetVolume(int volume);

    /// <summary>当前播放位置（秒）。宿主不支持时为 0（bug #10：进度条）。</summary>
    double PositionSec { get; }

    /// <summary>当前媒体总时长（秒）；未知（如直播流）为 0。</summary>
    double DurationSec { get; }

    /// <summary>跳转到指定位置（秒）。</summary>
    void Seek(double seconds);

    event Action? Ended;
}

/// <summary>
/// 音乐播放器（工具箱面板 14，规格 2.3）。
/// 三音源：本地文件夹（MP3/FLAC/OGG/WAV）、在线流媒体（预设 + 自定义）、MC 原声（自动提取 assets）。
/// 播放列表导航逻辑在 <see cref="MusicPlaylist"/>（Core），本 VM 负责状态、命令与三音源切换；
/// 实际解码交给注入的 <see cref="IMediaPlayer"/> 宿主（界面层 MediaElement）。
/// 迷你条（状态栏）与工具箱面板共用本单例。
/// </summary>
public class MusicPlayerViewModel : ObservableObject
{
    public static MusicPlayerViewModel Instance { get; } = new();

    private readonly MusicPlaylist _playlist = new();

    public ObservableCollection<Track> Tracks { get; } = new();

    // ★ Local / Online / McOst 之外新增 Client（本地客户端模式，规格实现项 6a）：
    //   该模式下启动器**不自己放**，音频由外部客户端（QQ 音乐 / 网易云 等）负责。
    private string _sourceKind = "Local"; // Local / Online / McOst / Client

    /// <summary>
    /// 共享的歌词引擎实例（规格实现项 4：叠加层与迷你播放条共用同一个，避免重复解析）。
    /// </summary>
    private readonly LyricEngine _lyric = new();

    /// <summary>在线歌词服务（客户端模式下没有本地 .lrc，歌词只能来自这里）。</summary>
    private readonly LyricService _lyricService = new();

    /// <summary>外部客户端可执行文件路径（用户选择，持久化在 profile）。</summary>
    private string _clientExePath = "";

    /// <summary>客户端模式状态说明（显示在流媒体页的状态卡片上）。</summary>
    private string _clientStatus = "";

    /// <summary>已拉起的客户端进程名列表（状态卡片展示）。</summary>
    private string _clientRunning = "";

    /// <summary>正在为客户端模式的后台拉词，标记避免重复请求。</summary>
    private CancellationTokenSource? _clientLyricCts;

    /// <summary>歌词引擎（供叠加层 / 迷你播放条绑定；单一实例，避免重复解析 LRC）。</summary>
    public LyricEngine Lyric => _lyric;

    /// <summary>本地客户端模式设置（含容错归一）。</summary>
    public MusicClientPrefs ClientPrefs =>
        (ProfileStore.Load(LauncherService.Instance.GameRoot).MusicClient
         ?? new MusicClientPrefs()).Normalized();
    private bool _isPlaying;
    private Track? _currentTrack;
    private string _statusText = "未播放";
    private int _volume = 60;
    private PlayMode _mode = PlayMode.LoopAll;
    private string _onlineUrl = "";
    private bool _autoDuck = true;
    private bool _resumeOnLaunch;
    private bool _expanded;
    private string _mcOstStatus = "";

    /// <summary>实际解码宿主（MediaElement），由主窗口注入。</summary>
    public IMediaPlayer? Host { get; set; }

    /// <summary>OGG/Vorbis 解码宿主（NAudio + NVorbis），用于 MediaElement 默认放不了的 .ogg（MC 原声 / 本地 OGG）。</summary>
    private readonly OggPlayer _oggHost = new();

    /// <summary>按当前曲目内容签名选择解码宿主：OGG/Vorbis -> NAudio 后端，否则 MediaElement。</summary>
    private IMediaPlayer? ActiveHost =>
        (CurrentTrack is { Path: var p } && OggPlayer.IsOggFile(p)) ? _oggHost : Host;

    /// <summary>
    /// 在线音源服务（<see cref="MusicSourceMode.Api"/>：由启动器自己去取流并播放）。
    /// <para>与歌词用的 LyricService 是两套东西：那个只负责歌词文本，
    /// 这个负责搜索 / 歌单 / 换播放直链，两者的数据源可以完全不同。</para>
    /// </summary>
    public OnlineMusicService Online { get; }

    /// <summary>在线音源是否可用（总开关打开且已填服务地址）。</summary>
    public bool ApiEnabled => Online.IsAvailable;

    /// <summary>搜索到的曲目。</summary>
    public ObservableCollection<OnlineTrack> OnlineResults { get; } = new();

    /// <summary>搜索到的公开歌单（数据源不支持搜索时恒为空）。</summary>
    public ObservableCollection<OnlinePlaylist> OnlinePlaylists { get; } = new();

    /// <summary>「我的歌单」（需登录且数据源支持登录）。</summary>
    public ObservableCollection<OnlinePlaylist> MyPlaylists { get; } = new();

    private string _onlineKeyword = "";
    private string _playlistIdInput = "";
    private bool _onlineBusy;
    private string _onlineStatus = "";
    private string _loginQrDataUrl = "";
    private bool _loginPanelVisible;
    private string _loginStatusText = "";
    private string? _loginKey;
    private CancellationTokenSource? _loginCts;

    /// <summary>当前正在播的在线曲目（在线歌词刷新要用它的 id 去取歌词）。</summary>
    private OnlineTrack? _currentOnline;

    /// <summary>搜索关键词。</summary>
    public string OnlineKeyword
    {
        get => _onlineKeyword;
        set => SetField(ref _onlineKeyword, value);
    }

    /// <summary>歌单 ID 输入框内容（Meting 协议没有「搜歌单」能力，只能手填 ID）。</summary>
    public string PlaylistIdInput
    {
        get => _playlistIdInput;
        set => SetField(ref _playlistIdInput, value);
    }

    public bool OnlineBusy
    {
        get => _onlineBusy;
        private set
        {
            if (SetField(ref _onlineBusy, value)) OnPropertyChanged(nameof(OnlineIdle));
        }
    }

    /// <summary>「非忙」：按钮 IsEnabled 绑它比再写一个反向转换器省事。</summary>
    public bool OnlineIdle => !_onlineBusy;

    /// <summary>在线音源操作的结果提示（按钮/列表下方的那行小字）。</summary>
    public string OnlineStatus
    {
        get => _onlineStatus;
        private set => SetField(ref _onlineStatus, value);
    }

    /// <summary>当前数据源是否支持扫码登录。</summary>
    public bool CanOnlineLogin => Online.CanLogin;

    public bool IsOnlineLoggedIn => Online.IsLoggedIn;

    /// <summary>账户展示文本（没昵称时退回「已登录 / 未登录」）。</summary>
    public string ApiAccountLabel
    {
        get
        {
            var nick = Online.Account?.Nickname;
            if (!string.IsNullOrWhiteSpace(nick)) return nick!;
            return Online.IsLoggedIn ? "已登录" : "未登录";
        }
    }

    /// <summary>二维码图片的 data URL；界面层解码成各自的图像类型。</summary>
    public string LoginQrDataUrl
    {
        get => _loginQrDataUrl;
        private set => SetField(ref _loginQrDataUrl, value);
    }

    public bool LoginPanelVisible
    {
        get => _loginPanelVisible;
        private set => SetField(ref _loginPanelVisible, value);
    }

    public string LoginStatusText
    {
        get => _loginStatusText;
        private set => SetField(ref _loginStatusText, value);
    }

    /// <summary>搜索在线歌曲。</summary>
    public ICommand SearchOnlineCommand { get; }

    /// <summary>导入在线歌单（参数为 <see cref="OnlinePlaylist"/>；留空则取输入框里的 ID）。</summary>
    public ICommand ImportPlaylistCommand { get; }

    /// <summary>点播一条搜索结果。</summary>
    public ICommand PlayOnlineTrackCommand { get; }

    public ICommand StartApiLoginCommand { get; }
    public ICommand CancelApiLoginCommand { get; }
    public ICommand ApiLogoutCommand { get; }
    public ICommand RefreshMyPlaylistsCommand { get; }

    /// <summary>MC 原声按分类分组（扫描后填充）。</summary>
    public ObservableCollection<McOstGroup> McOstGroups { get; } = new();

    private MusicPlayerViewModel()
    {
        PlayPauseCommand = new RelayCommand(_ => PlayPause());
        NextCommand = new RelayCommand(_ => Next());
        PreviousCommand = new RelayCommand(_ => Previous());
        LoadLocalFolderCommand = new RelayCommand(_ => LoadLocalFolder());
        SetSourceCommand = new RelayCommand(p => SetSource(p as string));
        SetModeCommand = new RelayCommand(_ => CycleMode());
        AddOnlineCommand = new RelayCommand(_ => AddOnline());
        ScanMcOstCommand = new RelayCommand(_ => ScanMcOst());
        PlayTrackCommand = new RelayCommand(PlayTrack);
        ExpandCommand = new RelayCommand(_ => Expanded = !Expanded);
        SeekCommand = new RelayCommand(p => Seek(p));
        RemoveTrackCommand = new RelayCommand(p => RemoveTrack(p as Track));
        LaunchClientCommand = new AsyncRelayCommand(_ => LaunchClientAsync());
        BrowseClientCommand = new RelayCommand(_ => BrowseClientExe());
        SyncClientTrackCommand = new RelayCommand(_ => _ = SyncClientLyricAsync());

        // ===== 在线音源（搜索 / 歌单 / 登录）=====
        SearchOnlineCommand = new AsyncRelayCommand(_ => SearchOnlineAsync());
        ImportPlaylistCommand = new AsyncRelayCommand(p => ImportPlaylistAsync(p as OnlinePlaylist));
        PlayOnlineTrackCommand = new AsyncRelayCommand(p => PlayOnlineTrackAsync(p as OnlineTrack));
        StartApiLoginCommand = new AsyncRelayCommand(_ => StartApiLoginAsync());
        CancelApiLoginCommand = new RelayCommand(_ => CancelApiLogin());
        ApiLogoutCommand = new RelayCommand(_ => ApiLogout());
        RefreshMyPlaylistsCommand = new AsyncRelayCommand(_ => LoadMyPlaylistsAsync());

        var profile = ProfileStore.Load(GameConstants.DefaultGameRoot);
        _autoDuck = profile.MusicAutoDuck;
        _volume = profile.MusicVolume;
        _resumeOnLaunch = profile.MusicResumeOnLaunch;
        _clientExePath = profile.MusicClientExePath ?? "";
        ClientStatus = HasClientExe ? $"当前客户端：{ClientDisplayName}" : "尚未选择客户端程序";

        // 在线音源：Reload 内部会从（已混淆的）凭证里恢复登录态。
        // Poseidon 注意顺序 —— 必须先于「恢复我的歌单」，否则出场就是未登录。
        Online = new OnlineMusicService(profile.MusicApi);
        Online.LoginStateChanged += () => RunOnUi(UpdateLoginUi);
        if (Online.IsLoggedIn && Online.CanLogin)
        {
            // 账户昵称/头像要联网才拿得到，放后台慢慢补：**不要**在这里 await，
            // 构造函数里 await 网络请求会把启动流程拖慢甚至卡住。
            _ = Task.Run(async () =>
            {
                if (await Online.RefreshAccountAsync()) PersistApiCredential();
                await LoadMyPlaylistsAsync();
            });
        }

        // bug：导入的本地音乐文件夹应在重启后保持——构造时把上次选择的文件夹重新载入播放列表，
        // 否则关闭再打开启动器后导入的歌曲全部丢失（仅断点续播的单曲会被恢复）。
        try
        {
            var lastFolder = profile.MusicLastFolder;
            if (!string.IsNullOrWhiteSpace(lastFolder) && Directory.Exists(lastFolder))
            {
                _playlist.AddFolder(lastFolder, recursive: true);
                SyncTracks();
            }
        }
        catch { /* 重载失败不影响其他功能 */ }

        // bug #10：进度条。播放期间每 500ms 同步一次播放位置，暂停/停止时停表。
        _progressTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _progressTimer.Tick += (_, _) => RefreshProgress();

        // P04：OGG 解码宿主自然播完时推进到下一曲（与 MediaElement 的 Ended 同一入口）。
        _oggHost.Ended += OnTrackEnded;
    }

    private readonly System.Windows.Threading.DispatcherTimer _progressTimer;
    private double _positionSec;
    private double _durationSec;
    private bool _isSeeking;

    /// <summary>用户正在点击 / 拖动进度条。拖拽期间置 true，暂停定时器回写位置，
    /// 否则 500ms 一次的 RefreshProgress 会把拖柄拽回实际播放位置（表现为拖动中回弹）。</summary>
    public bool IsSeeking
    {
        get => _isSeeking;
        set => _isSeeking = value;
    }

    /// <summary>当前播放位置（秒）。</summary>
    public double PositionSec
    {
        get => _positionSec;
        set
        {
            if (SetField(ref _positionSec, value))
            {
                OnPropertyChanged(nameof(PositionText));
                OnPropertyChanged(nameof(ProgressRatio));
            }
        }
    }

    /// <summary>当前曲目总时长（秒）：优先取解码器时长，其次取导入时读到的标签时长。</summary>
    public double DurationSec
    {
        get => _durationSec;
        set
        {
            if (SetField(ref _durationSec, value))
            {
                OnPropertyChanged(nameof(DurationText));
                OnPropertyChanged(nameof(ProgressRatio));
            }
        }
    }

    /// <summary>进度比例 0-100，供 Slider 绑定。</summary>
    public double ProgressRatio => DurationSec > 0 ? Math.Clamp(PositionSec / DurationSec * 100.0, 0, 100) : 0;

    public string PositionText => FormatTime(PositionSec);
    public string DurationText => FormatTime(DurationSec);

    /// <summary>是否有可显示的进度（时长已知）。</summary>
    public bool HasProgress => DurationSec > 0;

    private static string FormatTime(double sec) =>
        sec <= 0 ? "0:00" : TimeSpan.FromSeconds(sec).ToString(sec >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

    private void RefreshProgress()
    {
        var host = ActiveHost;
        if (host is null) return;

        // ★ 拖动期间**整段跳过**，连 DurationSec 也不能写。
        //   ProgressRatio 是计算属性（PositionSec / DurationSec），而 DurationSec 的 setter
        //   会 OnPropertyChanged(ProgressRatio)。若在拖动中更新 DurationSec（哪怕值没变、
        //   SetField 因相等而返回 false 不会通知，但时长从 host 读回的真实值一旦与本地不同就会通知），
        //   OneWay 绑定的 Slider 就会收到新的 ProgressRatio，把用户拖出来的位置**覆盖回去**
        //   —— 表现为「进度条被播放进度扯回去」，且与 IsSeeking 保护无关。
        if (_isSeeking) return;

        var hostDuration = host.DurationSec;
        if (hostDuration > 0) DurationSec = hostDuration;
        else if (CurrentTrack is { DurationSec: > 0 } t) DurationSec = t.DurationSec;

        PositionSec = host.PositionSec;

        // 歌词：与进度同频更新（1s 一跳足够，逐行切换观感与音乐歌词一致）
        RefreshLyric();
    }

    // ---- 歌词（规格实现项 3 / 4）----

    /// <summary>歌词固定方式跟随设置（切换隔离等场景下实时生效）。</summary>
    private LyricPinMode LyricPinModeSetting => ClientPrefs.LyricPin;

    // ---- 本地客户端模式（规格实现项 2 / 6.1）----

    /// <summary>外部客户端可执行文件路径（空 = 尚未选择）。</summary>
    public string ClientExePath
    {
        get => _clientExePath;
        private set
        {
            if (SetField(ref _clientExePath, value))
            {
                OnPropertyChanged(nameof(ClientDisplayName));
                OnPropertyChanged(nameof(HasClientExe));
                SavePrefs();
            }
        }
    }

    /// <summary>是否已选定客户端程序（流媒体页据此决定「选择程序」还是「启动」）。</summary>
    public bool HasClientExe => !string.IsNullOrWhiteSpace(_clientExePath);

    /// <summary>
    /// 「本地客户端模式」入口是否可用（规格联动规则：总开关关闭时后三项在 UI 中灰显或折叠）。
    /// 这里选折叠 —— 直接隐藏入口，用户不会点到一个注定被拒的模式。
    /// </summary>
    public bool ClientModeAvailable => ClientPrefs.Enabled;

    /// <summary>客户端显示名（取文件名，去扩展名）。</summary>
    public string ClientDisplayName
    {
        get
        {
            if (!HasClientExe) return "";
            try { return Path.GetFileNameWithoutExtension(_clientExePath); }
            catch { return _clientExePath; }
        }
    }

    /// <summary>客户端模式状态说明（状态卡片正文）。</summary>
    public string ClientStatus
    {
        get => _clientStatus;
        private set => SetField(ref _clientStatus, value);
    }

    /// <summary>由启动器拉起 / 正在运行的客户端进程（状态卡片副文本）。</summary>
    public string ClientRunning
    {
        get => _clientRunning;
        private set => SetField(ref _clientRunning, value);
    }

    /// <summary>选择客户端可执行文件。</summary>
    private void BrowseClientExe()
    {
        var picked = UIService.PickFile("音乐客户端 (*.exe)|*.exe", "选择音乐客户端程序");
        if (string.IsNullOrWhiteSpace(picked)) return;
        ClientExePath = picked;
        ClientStatus = $"已选择客户端：{ClientDisplayName}";
    }

    /// <summary>
    /// 启动外部客户端并登记到生命周期管理器（规格实现项 2「再次拉起」）。
    /// <para>登记后，宽限期计时才会把这批进程纳入判定 —— 否则切走音源后客户端永远不会被回收。</para>
    /// </summary>
    private async Task LaunchClientAsync()
    {
        if (!HasClientExe)
        {
            ClientStatus = "尚未选择客户端程序";
            return;
        }
        if (!File.Exists(_clientExePath))
        {
            ClientStatus = "客户端程序不存在，请重新选择";
            ClientExePath = "";
            return;
        }

        try
        {
            var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _clientExePath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(_clientExePath) ?? Environment.CurrentDirectory,
            });
            if (proc is null)
            {
                ClientStatus = "启动客户端失败";
                return;
            }

            ClientLifecycleService.Register(proc.Id, proc.ProcessName);
            ClientLifecycleService.Instance.Start();
            ClientRunning = $"运行中：{proc.ProcessName}（PID {proc.Id}）";
            ClientStatus = $"已启动 {proc.ProcessName}，切换到其它音源后若长期暂停将自动结束它";
        }
        catch (Exception ex)
        {
            ClientStatus = "启动客户端失败：" + ex.Message;
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// 为客户端模式的后台曲目拉取在线歌词。
    /// <para>
    /// 本地客户端模式没有可用的本地 <c>.lrc</c>（文件在客户端自己的音乐库里），
    /// 所以歌词必须来自 <see cref="LyricService"/>。查不到就静默清空，不报错。
    /// </para>
    /// </summary>
    private async Task SyncClientLyricAsync()
    {
        if (!IsLocalClient) return;
        if (!ClientPrefs.LyricEnabled) { _lyric.Clear(); RefreshLyricTexts(); return; }

        var title = CurrentTrack?.Title;
        var artist = CurrentTrack?.Artist;
        if (string.IsNullOrWhiteSpace(title))
        {
            _lyric.Clear();
            RefreshLyricTexts();
            return;
        }

        // 同一首歌已在拉取中就不重复发请求
        _clientLyricCts?.Cancel();
        var cts = new CancellationTokenSource();
        _clientLyricCts = cts;

        try
        {
            var lrc = await _lyricService.GetLrcAsync(title, artist, cts.Token);
            if (cts.IsCancellationRequested) return;

            _lyric.PinMode = LyricPinModeSetting;
            if (string.IsNullOrWhiteSpace(lrc)) _lyric.Clear();
            else _lyric.Load("api:" + title, LyricEngine.ParseLrc(lrc));

            // 客户端模式拿不到播放进度，按「当前行」显示即可（有歌词但不随进度跳）
            RefreshLyricTexts();
        }
        catch (OperationCanceledException) { /* 被新一轮请求取代，忽略 */ }
        catch { /* 歌词属装饰性，失败不影响播放 */ }
        finally
        {
            if (ReferenceEquals(_clientLyricCts, cts)) { _clientLyricCts.Dispose(); _clientLyricCts = null; }
        }
    }

    /// <summary>
    /// 切歌时载入对应 <c>.lrc</c>。找不到就清空 —— 规格要求「无歌词时静默隐藏，不弹错误提示」。
    /// </summary>
    private void LoadLyricForCurrentTrack()
    {
        try
        {
            // 本地客户端模式下，歌词是否显示由「客户端模式下仍用API获取歌词」决定；
            // 该开关关闭时清空（隐藏），开启时沿用已载入的歌词。
            if (IsLocalClient && !ClientPrefs.LyricEnabled) { _lyric.Clear(); return; }

            _lyric.PinMode = LyricPinModeSetting;

            // 本地客户端模式：文件在客户端自己的音乐库里，本地没有 .lrc —— 走在线 API
            if (IsLocalClient)
            {
                RefreshLyricTexts();
                _ = SyncClientLyricAsync();
                return;
            }

            // 在线曲目的 .lrc 不在本地，只能从数据源或在线歌词服务取
            if (IsOnline && _currentOnline is not null)
            {
                RefreshLyricTexts();
                _ = LoadOnlineLyricAsync(_currentOnline);
                return;
            }

            _lyric.TryLoadFromFile(CurrentTrack?.Path);
            RefreshLyricTexts();
        }
        catch
        {
            _lyric.Clear();
            RefreshLyricTexts();
        }
    }

    /// <summary>
    /// 按当前进度刷新歌词显示行（行数由「歌词固定方式」决定）。
    /// 行内容不变时<b>不通知</b>，避免每秒重建 StackPanel 造成闪烁。
    /// </summary>
    private void RefreshLyric()
    {
        try
        {
            _lyric.PinMode = LyricPinModeSetting;
            RefreshLyricTexts();
        }
        catch { /* 歌词属装饰性，失败不影响播放 */ }
    }

    private string _lyricText = "";
    private int _lyricLineCount;

    /// <summary>当前应显示的歌词文本（多行用换行分隔，供叠加层/迷你条 TextBlock）。</summary>
    public string LyricText
    {
        get => _lyricText;
        private set => SetField(ref _lyricText, value);
    }

    /// <summary>当前应显示的歌词行数（0 = 无歌词，界面应隐藏）。</summary>
    public int LyricLineCount
    {
        get => _lyricLineCount;
        private set
        {
            if (SetField(ref _lyricLineCount, value))
                OnPropertyChanged(nameof(HasLyric));
        }
    }

    /// <summary>是否有歌词可显示（界面据此隐藏歌词区，无需弹提示）。</summary>
    public bool HasLyric => LyricLineCount > 0;

    private void RefreshLyricTexts()
    {
        // 本地客户端模式下按设置决定是否隐藏
        if (IsLocalClient && !ClientPrefs.LyricEnabled)
        {
            LyricText = "";
            LyricLineCount = 0;
            return;
        }

        var lines = _lyric.GetDisplayLines(PositionSec);
        var text = string.Join(Environment.NewLine, lines.Select(l => l.Text));
        if (!string.Equals(text, LyricText, StringComparison.Ordinal))
            LyricText = text;
        LyricLineCount = lines.Count;
    }

    /// <summary>拖动进度条跳转（Slider 传来的值可能是比例或秒，按值域判断）。</summary>
    private void Seek(object? value)
    {
        var seconds = value switch
        {
            double d => d,
            int i => (double)i,
            string s when double.TryParse(s, out var parsed) => parsed,
            _ => 0
        };

        // 进度条绑定的是 0-100 的比例，超过 100 才按秒处理
        if (seconds > 100 && DurationSec > 0) seconds = seconds / 100.0 * DurationSec;
        if (DurationSec > 0 && seconds <= 100) seconds = seconds / 100.0 * DurationSec;

        // IsSeeking **不在拖动结束时复位**：拖动路径由 SeekInteraction 的
        // dragStart / dragEnd 独占管理，且 dragEnd 是在 commit **之后**才解除保护
        // （否则 500ms 的 RefreshProgress 会把拖柄拽回，表现为「进度条被播放进度扯回去」）。
        // 命令行调用（键盘 / 按钮跳转）没有 dragEnd 兜底，故按「进入前是否已在拖动」决定复位。
        var wasSeeking = _isSeeking;
        _isSeeking = true;
        try
        {
            SeekCore(seconds);
        }
        finally
        {
            // 拖动中（wasSeeking == true）交给 dragEnd 收尾，这里不动。
            if (!wasSeeking) _isSeeking = false;
        }
    }

    /// <summary>真正执行跳转（拖动保护的生命周期由调用方负责）。</summary>
    private void SeekCore(double seconds)
    {
        try
        {
            ActiveHost?.Seek(seconds);
            PositionSec = seconds;
        }
        catch { /* 播放后端未就绪时忽略 */ }
    }

    /// <summary>开始/停止进度刷新（与播放状态同步）。</summary>
    private void StartProgressTimer()
    {
        try { _progressTimer.Start(); } catch { }
    }

    private void StopProgressTimer()
    {
        try { _progressTimer.Stop(); } catch { }
    }

    public ICommand PlayPauseCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand LoadLocalFolderCommand { get; }
    public ICommand SetSourceCommand { get; }

    /// <summary>启动选定的外部音乐客户端（规格实现项 2：切回本地客户端模式时若未运行则拉起）。</summary>
    public ICommand LaunchClientCommand { get; }

    /// <summary>选择客户端可执行文件。</summary>
    public ICommand BrowseClientCommand { get; }

    /// <summary>手动重新拉取当前曲目的在线歌词。</summary>
    public ICommand SyncClientTrackCommand { get; }
    public ICommand SetModeCommand { get; }
    public ICommand AddOnlineCommand { get; }
    public ICommand ScanMcOstCommand { get; }
    public ICommand PlayTrackCommand { get; }
    public ICommand ExpandCommand { get; }

    /// <summary>拖动进度条跳转（bug #10）。</summary>
    public ICommand SeekCommand { get; }

    /// <summary>从播放列表删除指定曲目（本地与在线流媒体通用，bug2.txt #12）。</summary>
    public ICommand RemoveTrackCommand { get; }

    public string SourceKind
    {
        get => _sourceKind;
        private set
        {
            if (SetField(ref _sourceKind, value))
            {
                OnPropertyChanged(nameof(IsLocal));
                OnPropertyChanged(nameof(IsOnline));
                OnPropertyChanged(nameof(IsMcOst));
                OnPropertyChanged(nameof(IsLocalClient));
                SyncSourceMode();
            }
        }
    }

    public bool IsLocal => SourceKind == "Local";
    public bool IsOnline => SourceKind == "Online";
    public bool IsMcOst => SourceKind == "McOst";

    /// <summary>是否处于本地客户端模式（启动器不自己播放）。</summary>
    public bool IsLocalClient => SourceKind == "Client";

    public bool IsPlaying
    {
        get => _isPlaying;
        private set => SetField(ref _isPlaying, value);
    }

    public Track? CurrentTrack
    {
        get => _currentTrack;
        private set
        {
            if (SetField(ref _currentTrack, value))
            {
                OnPropertyChanged(nameof(CurrentTrackDisplay));
                OnPropertyChanged(nameof(CurrentTrackMetaText));
                OnPropertyChanged(nameof(HasTrack));
            }
        }
    }

    /// <summary>是否有已选中的曲目（用于状态栏迷你条显隐）。</summary>
    public bool HasTrack => CurrentTrack is not null;

    public string StatusText
    {
        get => _statusText;
        internal set => SetField(ref _statusText, value);
    }

    public int Volume
    {
        get => _volume;
        set
        {
            if (SetField(ref _volume, value))
            {
                ApplyVolume(value);
                SavePrefs();
            }
        }
    }

    public PlayMode Mode
    {
        get => _mode;
        private set => SetField(ref _mode, value);
    }

    public string OnlineUrl
    {
        get => _onlineUrl;
        set => SetField(ref _onlineUrl, value);
    }

    /// <summary>游戏启动时自动暂停 / 降音量（规格 2.3）。</summary>
    public bool AutoDuck
    {
        get => _autoDuck;
        set
        {
            if (SetField(ref _autoDuck, value))
                SavePrefs();
        }
    }

    /// <summary>启动时自动断点续播（bug #10）。</summary>
    public bool ResumeOnLaunch
    {
        get => _resumeOnLaunch;
        set
        {
            if (SetField(ref _resumeOnLaunch, value))
                SavePrefs();
        }
    }

    /// <summary>记录断点续播位置（当前曲目路径 + 进度秒），供下次启动恢复。</summary>
    private void SaveResumePoint()
    {
        try
        {
            var p = ProfileStore.Load(GameConstants.DefaultGameRoot);
            p.MusicLastTrack = CurrentTrack?.Path ?? "";
            p.MusicLastPosition = PositionSec;
            ProfileStore.Save(p);
        }
        catch { /* 忽略持久化失败 */ }
    }

    /// <summary>启动后尝试断点续播：若开启且存在上次曲目，则载入并跳到上次位置播放。</summary>
    public void RestoreLastState()
    {
        if (!_resumeOnLaunch) return;
        string? path = null;
        double pos = 0;
        try
        {
            var p = ProfileStore.Load(GameConstants.DefaultGameRoot);
            path = string.IsNullOrWhiteSpace(p.MusicLastTrack) ? null : p.MusicLastTrack;
            pos = p.MusicLastPosition;
        }
        catch { return; }
        if (path is null || !File.Exists(path)) return;

        var track = new Track { Path = path, Title = Path.GetFileNameWithoutExtension(path) };
        var meta = AudioMetadata.Read(path);
        if (meta != AudioTag.Empty)
        {
            track.Title = meta.Title ?? track.Title;
            track.Artist = meta.Artist;
            track.Album = meta.Album;
            track.DurationSec = meta.DurationSec;
        }
        _playlist.Add(track);
        SyncTracks();
        _playlist.Select(_playlist.Count - 1);
        CurrentTrack = _playlist.Current;
        PlayOnActiveHost(path);
        try { ActiveHost?.Seek(pos); } catch { }
        PositionSec = pos;
        DurationSec = track.DurationSec;
        LoadLyricForCurrentTrack();     // 续播同样要载入歌词
        IsPlaying = true;
        StartProgressTimer();
        StatusText = "已续播：" + CurrentTrack.Display;
        OnPropertyChanged(nameof(CurrentTrackDisplay));
    }

    /// <summary>状态栏迷你条是否展开为完整列表。</summary>
    public bool Expanded
    {
        get => _expanded;
        set => SetField(ref _expanded, value);
    }

    public string McOstStatus
    {
        get => _mcOstStatus;
        private set => SetField(ref _mcOstStatus, value);
    }

    public string CurrentTrackDisplay => CurrentTrack?.Display ?? "未选择曲目";

    /// <summary>当前曲目副标题（歌手 · 专辑），避免 Run 内多段绑定在部分主题下不刷新。</summary>
    public string CurrentTrackMetaText => CurrentTrack?.MetaText ?? "";

    // ---- 命令实现 ----

    private void SetSource(string? kind)
    {
        if (kind == "Online" && !ApiEnabled)
        {
            StatusText = "在线音源未启用，请先在「设置 → 音乐」中配置";
            return;
        }

        if (kind is "Local" or "Online" or "McOst" or "Client")
        {
            // 本地客户端模式总开关关闭时降级为 API（规格：关闭时完全不涉及外部客户端进程）
            if (kind == "Client" && !ClientPrefs.Enabled)
            {
                StatusText = "本地客户端模式未启用，请先在「设置 → 音乐」中开启";
                return;
            }

            SourceKind = kind!;
            if (kind == "McOst" && McOstGroups.Count == 0)
                ScanMcOst();
            if (kind == "Client")
            {
                // 本地客户端模式下启动器不放音：停掉自己的音源并清空播放列表，
                // 避免与外部客户端叠音（规格不变量：同一时间只有一个音源在播放）。
                StopLocalPlaybackForClientMode();
                RefreshLyricTexts();   // 按「客户端模式下仍用API取歌词」决定显隐
                StatusText = "本地客户端模式：音乐由外部客户端播放，启动器不再输出音频";

                // 规格实现项 2「再次拉起」：切回本地客户端模式时，若客户端未运行则启动它
                ClientStatus = HasClientExe
                    ? $"当前客户端：{ClientDisplayName}"
                    : "尚未选择客户端程序";
                _ = SyncClientLyricAsync();
            }
        }
    }

    /// <summary>
    /// 把当前 <see cref="SourceKind"/> 同步到 <see cref="MusicModeManager"/>（规格实现项 5）。
    /// <para>映射：Local → <see cref="MusicSourceMode.LocalFolder"/>、
    /// Online → <see cref="MusicSourceMode.Api"/>、
    /// Client → <see cref="MusicSourceMode.LocalClient"/>。
    /// McOst（MC 原声）视为本地文件夹的一种，故也映射到 LocalFolder。</para>
    /// <para>切换时 <see cref="MusicModeManager"/> 会停掉上一个音源（防叠音），
    /// 并广播模式变更给生命周期管理器 / 歌词引擎 / 叠加层。</para>
    /// </summary>
    private void SyncSourceMode()
    {
        try
        {
            var target = SourceKind switch
            {
                "Online" => MusicSourceMode.Api,
                "Client" => MusicSourceMode.LocalClient,
                _ => MusicSourceMode.LocalFolder,
            };
            var prefs = ClientPrefs;
            MusicModeManager.StopLocalPlayback = StopLocalPlaybackForClientMode;
            MusicModeManager.SwitchTo(target, prefs);
        }
        catch
        {
            // 模式同步属非关键，失败不影响播放
        }
    }

    /// <summary>
    /// 停掉启动器自己的音源（供 <see cref="MusicModeManager.SwitchTo"/> 与
    /// 切入本地客户端模式时调用）。**不抛异常** —— 模式切换不应因停止失败而中断。
    /// </summary>
    private void StopLocalPlaybackForClientMode()
    {
        try
        {
            if (IsPlaying) ActiveHost?.Pause();
            StopProgressTimer();
            IsPlaying = false;
        }
        catch { /* 停止失败不影响模式切换 */ }
    }

    private void PlayPause()
    {
        if (CurrentTrack is null)
        {
            if (Tracks.Count == 0) { StatusText = "播放列表为空，请先加载音源"; return; }
            SelectAndPlay(0);
            return;
        }
        if (IsPlaying)
        {
            IsPlaying = false;
            ActiveHost?.Pause();
            StopProgressTimer();
            SaveResumePoint();
            StatusText = "已暂停：" + CurrentTrack.Display;
        }
        else
        {
            IsPlaying = true;
            ActiveHost?.Resume();
            StartProgressTimer();
            StatusText = "正在播放：" + CurrentTrack.Display;
        }
    }

    private void Next() => Advance(userTriggered: true);
    private void Previous() => Advance(userTriggered: true, backward: true);

    private void Advance(bool userTriggered, bool backward = false)
    {
        var t = backward ? _playlist.Previous() : _playlist.Next(userTriggered);
        if (t is null)
        {
            IsPlaying = false;
            ActiveHost?.Stop();
            StatusText = "播放列表结束";
            return;
        }
        SelectAndPlay(_playlist.CurrentIndex);
    }

    /// <summary>宿主报告一曲播放结束（顺序播放到尾则停止）。</summary>
    public void OnTrackEnded()
    {
        var t = _playlist.Next(userTriggered: false);
        if (t is null)
        {
            IsPlaying = false;
            ActiveHost?.Stop();
            StopProgressTimer();
            StatusText = "播放列表结束";
            return;
        }
        SelectAndPlay(_playlist.CurrentIndex);
    }

    /// <summary>P04：按内容签名把音源路由到正确解码后端，并停掉另一个后端避免串声。</summary>
    private void PlayOnActiveHost(string path)
    {
        if (OggPlayer.IsOggFile(path))
        {
            Host?.Stop();
            _oggHost.LoadAndPlay(path);
        }
        else
        {
            _oggHost.Stop();
            Host?.LoadAndPlay(path);
        }
    }

    private void SelectAndPlay(int index)
    {
        if (index < 0 || index >= _playlist.Count) return;
        _playlist.Select(index);
        CurrentTrack = _playlist.Current;
        IsPlaying = true;
        PlayOnActiveHost(CurrentTrack!.Path);
        StartProgressTimer();
        SaveResumePoint();
        LoadLyricForCurrentTrack();     // 切歌 → 载入对应 .lrc
        StatusText = "正在播放：" + CurrentTrack.Display;
        OnPropertyChanged(nameof(CurrentTrackDisplay));
    }

    private void CycleMode()
    {
        Mode = _playlist.CycleMode();
        StatusText = "循环模式：" + Mode switch
        {
            PlayMode.Sequential => "顺序",
            PlayMode.LoopAll => "列表循环",
            PlayMode.LoopOne => "单曲循环",
            PlayMode.Shuffle => "随机",
            _ => Mode.ToString()
        };
    }

    /// <summary>直接播放指定曲目（来自 MC 原声列表或本地列表）。</summary>
    private void PlayTrack(object? param)
    {
        Track? track = param switch
        {
            Track t => t,
            McOstTrack m => m.ToTrack(),
            _ => null
        };
        if (track is null) return;
        int idx = _playlist.Tracks.ToList().IndexOf(track);
        if (idx < 0)
        {
            _playlist.Add(track);
            SyncTracks();
            idx = _playlist.Count - 1;
        }
        SelectAndPlay(idx);
    }

    /// <summary>从播放列表删除指定曲目（本地与在线流媒体通用，bug2.txt #12）。
    /// 若删除的是当前播放曲目，则停止播放并切换到调整后的当前曲（不自动续播）。</summary>
    private void RemoveTrack(Track? track)
    {
        if (track is null) return;
        int idx = _playlist.Tracks.ToList().IndexOf(track);
        if (idx < 0) return;

        bool isCurrent = CurrentTrack is not null && ReferenceEquals(_playlist.Tracks[idx], CurrentTrack);
        if (isCurrent)
        {
            IsPlaying = false;
            ActiveHost?.Stop();
            StopProgressTimer();
        }

        _playlist.RemoveAt(idx);

        if (isCurrent)
        {
            if (_playlist.Count > 0)
            {
                int next = _playlist.CurrentIndex >= 0 ? _playlist.CurrentIndex : 0;
                _playlist.Select(next);
                CurrentTrack = _playlist.Current;
                StatusText = "已删除当前曲目，已切到下一首";
            }
            else
            {
                CurrentTrack = null;
                StatusText = "播放列表已清空";
            }
        }
        else
        {
            StatusText = "已删除曲目";
        }
        SyncTracks();
    }

    private void LoadLocalFolder()
    {
        // 上次打开的本地音乐文件夹作为起始目录（无则默认）
        string? initial = null;
        try
        {
            var p = ProfileStore.Load(GameConstants.DefaultGameRoot);
            initial = string.IsNullOrWhiteSpace(p.MusicLastFolder) ? null : p.MusicLastFolder;
        }
        catch { /* 忽略读取失败，按默认处理 */ }

        var folder = UIService.PickFolder("选择音乐文件夹", initial);
        if (string.IsNullOrEmpty(folder)) return;

        // 记住本次选择的文件夹，供下次打开定位
        try
        {
            var p = ProfileStore.Load(GameConstants.DefaultGameRoot);
            p.MusicLastFolder = folder;
            ProfileStore.Save(p);
        }
        catch { /* 忽略持久化失败 */ }

        var added = _playlist.AddFolder(folder, recursive: true);
        SyncTracks();
        StatusText = added > 0 ? $"已添加 {added} 首本地曲目" : "未找到支持的音频文件";
    }

    private void AddOnline()
    {
        if (!ApiEnabled) { StatusText = "在线音源未启用，请先在「设置 → 音乐」中配置"; return; }
        if (string.IsNullOrWhiteSpace(OnlineUrl)) { StatusText = "请填写在线流媒体地址"; return; }
        var track = new Track
        {
            Path = OnlineUrl,
            Title = OnlineUrl,
            Artist = "在线流媒体"
        };
        _playlist.Add(track);
        SyncTracks();
        StatusText = "已添加在线音源";
    }

    private void ScanMcOst()
    {
        var root = LauncherService.Instance.GameRoot;
        var groups = McOstExtractor.Scan(root);
        McOstGroups.Clear();
        foreach (var g in groups) McOstGroups.Add(g);

        var total = groups.Sum(g => g.Tracks.Count);
        McOstStatus = total > 0 ? $"已提取 {total} 首 MC 原声" : "未找到 MC 原声（assets 缺失或不支持）";
    }

    private void SyncTracks()
    {
        Tracks.Clear();
        foreach (var t in _playlist.Tracks) Tracks.Add(t);
        OnPropertyChanged(nameof(CurrentTrackDisplay));
    }

    /// <summary>游戏启动联动：依照设置自动暂停 / 降音量。</summary>
    public void OnGameLaunch()
    {
        if (!AutoDuck) return;
        if (IsPlaying)
        {
            // 降音量（保留播放）：MC 启动时背景音乐调小
            ApplyVolume(Math.Min(Volume, 15));
            StatusText = "游戏启动：音乐已降低音量";
        }
    }

    /// <summary>游戏退出联动：恢复音量。</summary>
    public void OnGameExit()
    {
        if (!AutoDuck) return;
        ApplyVolume(Volume);
    }

    /// <summary>把音量同时推送到两个解码后端（MediaElement + OGG/NAudio），保持二者一致。</summary>
    private void ApplyVolume(int v)
    {
        Host?.SetVolume(v);
        _oggHost.SetVolume(v);
    }

    /// <summary>宿主注入后把当前音量推送到解码器（构造函数里 Host 尚为空）。</summary>
    public void SetVolumeFromHost() => ApplyVolume(_volume);

    /// <summary>
    /// 设置页改了本地客户端模式的任一项后由外部调用，让播放器即时生效：
    /// 歌词固定方式（行数）、歌词显隐、以及当前模式是否还成立。
    /// </summary>
    /// <remarks>
    /// 总开关被关掉时若正处于客户端模式，需退回本地文件夹 —— 否则会停在一个
    /// 「模式叫客户端但开关已关」的矛盾状态（歌词显隐、生命周期判定全按开关走）。
    /// </remarks>
    public void OnClientPrefsChanged()
    {
        try
        {
            if (IsLocalClient && !ClientPrefs.Enabled)
            {
                SourceKind = "Local";
                StatusText = "本地客户端模式已关闭，已切回本地文件夹";
            }

            _lyric.PinMode = ClientPrefs.LyricPin;
            OnPropertyChanged(nameof(ClientModeAvailable));

            if (IsLocalClient)
            {
                if (ClientPrefs.LyricEnabled) _ = SyncClientLyricAsync();
                else { _lyric.Clear(); RefreshLyricTexts(); }
            }
            else
            {
                RefreshLyricTexts();
            }
        }
        catch
        {
            // 设置联动属非关键，失败不影响播放
        }
    }

    // ===== 在线音源（规格：MusicSourceMode.Api）=====

    /// <summary>把后台线程的 UI 通知送回 UI 线程（事件回调可能来自任何线程）。</summary>
    private static void RunOnUi(Action action)
    {
        try
        {
            var d = System.Windows.Application.Current?.Dispatcher;
            if (d is null || d.CheckAccess()) action();
            else d.Invoke(action);
        }
        catch { /* 应用退出时的回调无所谓 */ }
    }

    private async Task SearchOnlineAsync()
    {
        if (OnlineBusy) return;
        if (!ApiEnabled) { OnlineStatus = "在线音源未启用"; return; }

        var kw = (OnlineKeyword ?? "").Trim();
        if (kw.Length == 0) { OnlineStatus = "请输入歌曲名"; return; }

        OnlineBusy = true;
        OnlineStatus = "搜索中…";
        try
        {
            var songs = await Online.SearchSongsAsync(kw);
            OnlineResults.Clear();
            foreach (var s in songs) OnlineResults.Add(s);
            OnlineStatus = songs.Count > 0 ? $"找到 {songs.Count} 首" : "没有找到匹配的歌曲";

            OnlinePlaylists.Clear();
            if (Online.CanSearchPlaylists)
            {
                foreach (var pl in await Online.SearchPlaylistsAsync(kw)) OnlinePlaylists.Add(pl);
            }
        }
        catch (MusicApiException ex)
        {
            OnlineStatus = ex.Message;
        }
        catch
        {
            OnlineStatus = "搜索失败，请检查网络或数据源配置";
        }
        finally { OnlineBusy = false; }
    }

    /// <summary>
    /// 导入歌单到播放列表。
    /// <para>歌单里的每首都要再取一次直链 —— 直链有有效期，且与所选音质绑定，
    /// 现在取好是为了让列表里的曲目随时可点播。</para>
    /// </summary>
    private async Task ImportPlaylistAsync(OnlinePlaylist? playlist)
    {
        if (OnlineBusy) return;
        if (!ApiEnabled) { OnlineStatus = "在线音源未启用"; return; }

        var id = playlist?.Id ?? (PlaylistIdInput ?? "").Trim();
        if (string.IsNullOrWhiteSpace(id)) { OnlineStatus = "请先搜索并选择一个歌单，或填写歌单 ID"; return; }

        OnlineBusy = true;
        OnlineStatus = "正在读取歌单…";
        try
        {
            var tracks = await Online.GetPlaylistTracksAsync(id);
            if (tracks.Count == 0) { OnlineStatus = "歌单为空或 ID 不正确"; return; }

            OnlineStatus = $"歌单共 {tracks.Count} 首，正在获取播放地址…";
            var added = await MaterializeAsync(tracks);
            OnlineStatus = added > 0
                ? $"已导入 {added} 首" + (added < tracks.Count ? $"（{tracks.Count - added} 首因版权无法获取）" : "")
                : "没有可播放的曲目（多为版权限制）";
        }
        catch (MusicApiException ex) { OnlineStatus = ex.Message; }
        catch { OnlineStatus = "读取歌单失败"; }
        finally { OnlineBusy = false; }
    }

    /// <summary>
    /// 并发换直链、收集完再按原顺序加入播放列表。
    /// <para>为什么「先并发取 URL，最后统一 Add」：<see cref="MusicPlaylist"/> 不是线程安全的，
    /// 并发 Add 会打乱 CurrentIndex 语义；而 URL 请求是纯网络，并行能省下大量等待。</para>
    /// </summary>
    private async Task<int> MaterializeAsync(IReadOnlyList<OnlineTrack> tracks, int maxParallel = 8)
    {
        var urls = new string?[tracks.Count];
        using var sem = new SemaphoreSlim(maxParallel);

        await Task.WhenAll(tracks.Select((t, i) => Task.Run(async () =>
        {
            await sem.WaitAsync();
            try { urls[i] = await Online.GetPlayableUrlAsync(t); }
            catch { urls[i] = null; }
            finally { sem.Release(); }
        })));

        var added = 0;
        for (var i = 0; i < tracks.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(urls[i])) continue;
            _playlist.Add(tracks[i].ToTrack(urls[i]!));
            added++;
        }
        SyncTracks();
        return added;
    }

    /// <summary>点播一条搜索结果（先换直链，再进播放列表播放）。</summary>
    private async Task PlayOnlineTrackAsync(OnlineTrack? track)
    {
        if (track is null || OnlineBusy) return;
        if (!ApiEnabled) { OnlineStatus = "在线音源未启用"; return; }

        OnlineBusy = true;
        OnlineStatus = "正在获取播放地址…";
        try
        {
            // 重复点同一首时不要往列表里加第二份：按「标题 + 歌手」找已有条目
            var existing = FindOnlineInPlaylist(track);
            if (existing >= 0)
            {
                _currentOnline = track;
                SelectAndPlay(existing);
                OnlineStatus = "正在播放：" + (CurrentTrack?.Display ?? track.Title);
                return;
            }

            var url = await Online.GetPlayableUrlAsync(track);
            if (string.IsNullOrWhiteSpace(url))
            {
                OnlineStatus = "无法获取播放地址（多为版权或会员限制）";
                return;
            }

            SourceKind = "Online";
            _currentOnline = track;
            _playlist.Add(track.ToTrack(url!));
            SyncTracks();
            SelectAndPlay(_playlist.Count - 1);
            OnlineStatus = "正在播放：" + CurrentTrack!.Display;
        }
        catch (MusicApiException ex) { OnlineStatus = ex.Message; }
        catch { OnlineStatus = "播放失败"; }
        finally { OnlineBusy = false; }
    }

    /// <summary>在播放列表里找同一首在线曲目（标题 + 歌手都相等）。返回 -1 表示没有。</summary>
    private int FindOnlineInPlaylist(OnlineTrack track)
    {
        var list = _playlist.Tracks.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].IsRemote && list[i].Title == track.Title && list[i].Artist == track.Artist)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// 取在线曲目的歌词：先看数据源给不给（部分数据源列表接口就带 lrc），
    /// 拿不到再退到在线歌词服务。<b>全程静默</b> —— 没歌词就隐藏歌词区，不弹错误。
    /// </summary>
    private async Task LoadOnlineLyricAsync(OnlineTrack track)
    {
        _clientLyricCts?.Cancel();
        var cts = new CancellationTokenSource();
        _clientLyricCts = cts;
        try
        {
            var lrc = await Online.GetLyricAsync(track, cts.Token);
            if (string.IsNullOrWhiteSpace(lrc) && ClientPrefs.LyricEnabled)
                lrc = await _lyricService.GetLrcAsync(track.Title, track.Artist, cts.Token);
            if (cts.IsCancellationRequested) return;

            _lyric.PinMode = LyricPinModeSetting;
            if (string.IsNullOrWhiteSpace(lrc)) _lyric.Clear();
            else _lyric.Load("online:" + track.Id, LyricEngine.ParseLrc(lrc));

            RefreshLyricTexts();
        }
        catch (OperationCanceledException) { /* 被新一轮取代 */ }
        catch { /* 歌词属装饰性，失败不影响播放 */ }
        finally
        {
            if (ReferenceEquals(_clientLyricCts, cts)) { _clientLyricCts.Dispose(); _clientLyricCts = null; }
        }
    }

    // ---- 登录 ----

    private async Task StartApiLoginAsync()
    {
        if (!Online.CanLogin)
        {
            LoginStatusText = "当前数据源不支持扫码登录，请改用网易云 API";
            return;
        }
        if (!Online.IsAvailable)
        {
            LoginStatusText = "在线音源不可用，请先配置服务地址";
            return;
        }

        CancelApiLogin();
        var cts = new CancellationTokenSource();
        _loginCts = cts;
        try
        {
            LoginStatusText = "正在获取二维码…";
            var session = await Online.CreateQrLoginAsync(cts.Token);
            _loginKey = session.Key;
            LoginQrDataUrl = session.QrDataUrl;
            LoginPanelVisible = true;
            LoginStatusText = "请用手机扫描下方二维码";

            // 轮询节奏 1.5s：太快会被服务端限流，太慢用户会觉得「点了没反应」
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(1500, cts.Token);
                var r = await Online.PollQrLoginAsync(session.Key, cts.Token);
                LoginStatusText = r.Message;

                if (r.State == QrLoginState.Confirmed)
                {
                    await Online.CompleteLoginAsync(r.Cookie, cts.Token);
                    PersistApiCredential();
                    LoginPanelVisible = false;
                    LoginQrDataUrl = "";
                    await LoadMyPlaylistsAsync();
                    UpdateLoginUi();
                    break;
                }
                if (r.State == QrLoginState.Expired) break;
            }
        }
        catch (OperationCanceledException) { /* 用户取消 */ }
        catch (MusicApiException ex) { LoginStatusText = ex.Message; }
        catch { LoginStatusText = "登录失败，请重试"; }
        finally
        {
            if (ReferenceEquals(_loginCts, cts)) { _loginCts.Dispose(); _loginCts = null; }
        }
    }

    private void CancelApiLogin()
    {
        try { _loginCts?.Cancel(); } catch { }
        _loginCts = null;
        _loginKey = null;
        LoginPanelVisible = false;
        LoginQrDataUrl = "";
    }

    private void ApiLogout()
    {
        Online.Logout();
        MyPlaylists.Clear();
        PersistApiCredential();
        UpdateLoginUi();
        LoginStatusText = "已退出登录";
    }

    private async Task LoadMyPlaylistsAsync()
    {
        if (!Online.IsLoggedIn || !Online.CanLogin) return;
        try
        {
            var list = await Online.GetMyPlaylistsAsync();
            MyPlaylists.Clear();
            foreach (var p in list) MyPlaylists.Add(p);
        }
        catch { /* 登录态失效：静默清空即可 */ }
    }

    /// <summary>把当前登录态（凭证 + 昵称头像）写回配置。</summary>
    private void PersistApiCredential()
    {
        try
        {
            var p = ProfileStore.Load(GameConstants.DefaultGameRoot);
            var exp = Online.ExportCredential();
            p.MusicApi.Credential = exp?.Credential ?? "";
            p.MusicApi.AccountName = exp?.Nickname ?? "";
            p.MusicApi.AccountAvatar = exp?.AvatarUrl ?? "";
            ProfileStore.Save(p);
        }
        catch { /* 持久化失败不影响本次会话 */ }
    }

    private void UpdateLoginUi()
    {
        OnPropertyChanged(nameof(IsOnlineLoggedIn));
        OnPropertyChanged(nameof(CanOnlineLogin));
        OnPropertyChanged(nameof(ApiAccountLabel));
    }

    /// <summary>
    /// 设置页改了在线音源的任一项后调用：重建数据源并让界面即时反映。
    /// <para><b>为什么要重建而不是改字段</b>：协议类型 / 服务地址都变了的话，
    /// provider 内部状态（包括登录态）必须整体重来。</para>
    /// </summary>
    public void OnApiPrefsChanged()
    {
        try
        {
            var prefs = ProfileStore.Load(GameConstants.DefaultGameRoot).MusicApi;
            Online.Reload(prefs);

            OnPropertyChanged(nameof(ApiEnabled));
            UpdateLoginUi();

            // 在线音源被关掉时，若正停在该音源则退回本地文件夹
            if (IsOnline && !ApiEnabled)
            {
                SourceKind = "Local";
                StatusText = "在线音源已关闭，已切回本地文件夹";
            }

            _ = LoadMyPlaylistsAsync();
        }
        catch { /* 联动属非关键 */ }
    }
    private void SavePrefs()
    {
        try
        {
            var p = ProfileStore.Load(GameConstants.DefaultGameRoot);
            p.MusicAutoDuck = _autoDuck;
            p.MusicVolume = _volume;
            p.MusicResumeOnLaunch = _resumeOnLaunch;
            p.MusicClientExePath = _clientExePath;
            ProfileStore.Save(p);
        }
        catch { /* 忽略持久化失败 */ }
    }
}
