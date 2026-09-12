using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
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

    private string _sourceKind = "Local"; // Local / Online / McOst
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

    public ObservableCollection<string> OnlinePresets { get; } = new()
    {
        "https://stream.example.com/minecraft-radio",
        "https://radio.example.org/ambient"
    };

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
        PlayTrackCommand = new RelayCommand(p => PlayTrack(p as Track));
        ExpandCommand = new RelayCommand(_ => Expanded = !Expanded);
        SeekCommand = new RelayCommand(p => Seek(p));
        RemoveTrackCommand = new RelayCommand(p => RemoveTrack(p as Track));

        var profile = ProfileStore.Load(GameConstants.DefaultGameRoot);
        _autoDuck = profile.MusicAutoDuck;
        _volume = profile.MusicVolume;
        _resumeOnLaunch = profile.MusicResumeOnLaunch;

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
    }

    private readonly System.Windows.Threading.DispatcherTimer _progressTimer;
    private double _positionSec;
    private double _durationSec;
    private bool _isSeeking;

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
        var host = Host;
        if (host is null) return;

        var hostDuration = host.DurationSec;
        if (hostDuration > 0) DurationSec = hostDuration;
        else if (CurrentTrack is { DurationSec: > 0 } t) DurationSec = t.DurationSec;

        if (!_isSeeking) PositionSec = host.PositionSec;
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

        try
        {
            _isSeeking = true;
            Host?.Seek(seconds);
            PositionSec = seconds;
        }
        finally
        {
            _isSeeking = false;
        }
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
            }
        }
    }

    public bool IsLocal => SourceKind == "Local";
    public bool IsOnline => SourceKind == "Online";
    public bool IsMcOst => SourceKind == "McOst";

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
                Host?.SetVolume(value);
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
        Host?.LoadAndPlay(path);
        try { Host?.Seek(pos); } catch { }
        PositionSec = pos;
        DurationSec = track.DurationSec;
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
        if (kind is "Local" or "Online" or "McOst")
        {
            SourceKind = kind!;
            if (kind == "McOst" && McOstGroups.Count == 0)
                ScanMcOst();
        }
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
            Host?.Pause();
            StopProgressTimer();
            SaveResumePoint();
            StatusText = "已暂停：" + CurrentTrack.Display;
        }
        else
        {
            IsPlaying = true;
            Host?.Resume();
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
            Host?.Stop();
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
            Host?.Stop();
            StopProgressTimer();
            StatusText = "播放列表结束";
            return;
        }
        SelectAndPlay(_playlist.CurrentIndex);
    }

    private void SelectAndPlay(int index)
    {
        if (index < 0 || index >= _playlist.Count) return;
        _playlist.Select(index);
        CurrentTrack = _playlist.Current;
        IsPlaying = true;
        Host?.LoadAndPlay(CurrentTrack!.Path);
        StartProgressTimer();
        SaveResumePoint();
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
    private void PlayTrack(Track? track)
    {
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
            Host?.Stop();
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
            Host?.SetVolume(Math.Min(Volume, 15));
            StatusText = "游戏启动：音乐已降低音量";
        }
    }

    /// <summary>游戏退出联动：恢复音量。</summary>
    public void OnGameExit()
    {
        if (!AutoDuck) return;
        Host?.SetVolume(Volume);
    }

    /// <summary>宿主注入后把当前音量推送到解码器（构造函数里 Host 尚为空）。</summary>
    public void SetVolumeFromHost() => Host?.SetVolume(_volume);

    private void SavePrefs()
    {
        try
        {
            var p = ProfileStore.Load(GameConstants.DefaultGameRoot);
            p.MusicAutoDuck = _autoDuck;
            p.MusicVolume = _volume;
            p.MusicResumeOnLaunch = _resumeOnLaunch;
            ProfileStore.Save(p);
        }
        catch { /* 忽略持久化失败 */ }
    }
}
