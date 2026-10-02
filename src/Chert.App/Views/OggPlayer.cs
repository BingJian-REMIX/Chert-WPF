using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using NAudio.Wave;
using NAudio.Vorbis;

namespace Chert.App.ViewModels;

/// <summary>
/// 用 NAudio（<see cref="WaveOutEvent"/> 输出）+ NVorbis（<see cref="VorbisWaveReader"/> 解码）实现
/// <see cref="IMediaPlayer"/>，专门播放 Windows Media Foundation 默认不支持的 OGG（Vorbis）音频——
/// 主要是 MC 原声（assets/objects 下无扩展名的哈希文件）与本地 .ogg。
/// 其余格式（MP3/WAV/M4A 等）仍走 <see cref="MediaElementPlayer"/>，由视图模型按内容签名分流。
/// <para>设计要点：每次播放 / 恢复都新建一个 <see cref="WaveOutEvent"/> 实例（Init 只能调一次），
/// 暂停 / 停止只需释放当前输出设备并保留解码器，避免依赖不同 NAudio 版本间 Pause/Resume 的差异。</para>
/// </summary>
public sealed class OggPlayer : IMediaPlayer
{
    private readonly object _gate = new();
    private VorbisWaveReader? _reader;
    private WaveOutEvent? _output;
    private double _pausedPosition;
    private bool _manualStop;
    private int _volume = 60;
    private readonly Dispatcher? _uiDispatcher = Application.Current?.Dispatcher;

    public event Action? Ended;

    /// <summary>是否为 OGG/Vorbis 音频（按扩展名或文件头 "OggS" 魔数判断）。</summary>
    public static bool IsOggFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> hdr = stackalloc byte[4];
            if (fs.Read(hdr) >= 4 && hdr[0] == 'O' && hdr[1] == 'g' && hdr[2] == 'g' && hdr[3] == 'S')
                return true;
        }
        catch { /* 打不开就当作非 OGG，交给 MediaElement 处理（也会失败并提示） */ }
        return false;
    }

    public void LoadAndPlay(string path)
    {
        StopInternal(raiseEnded: false);
        try
        {
            var reader = new VorbisWaveReader(path);
            var output = new WaveOutEvent();
            output.Volume = ClampVolume(_volume);
            output.PlaybackStopped += OnPlaybackStopped;
            output.Init(reader);
            lock (_gate)
            {
                _reader = reader;
                _output = output;
                _pausedPosition = 0;
            }
            output.Play();
        }
        catch (Exception ex)
        {
            MusicPlayerViewModel.Instance.StatusText = "OGG 解码失败：" + ex.Message;
        }
    }

    public void Pause()
    {
        WaveOutEvent? output;
        lock (_gate)
        {
            if (_output is null || _reader is null) return;
            _pausedPosition = _reader.CurrentTime.TotalSeconds;
            output = _output;
            _output = null;
        }
        // 释放输出设备（会触发 PlaybackStopped，以 _manualStop 标记忽略），解码器保留以便恢复。
        _manualStop = true;
        try { output.Stop(); } catch { }
        try { output.Dispose(); } catch { }
    }

    public void Resume()
    {
        VorbisWaveReader? reader;
        lock (_gate)
        {
            if (_reader is null || _output is not null) return;
            reader = _reader;
        }
        try
        {
            reader.CurrentTime = TimeSpan.FromSeconds(Math.Max(0, _pausedPosition));
            var output = new WaveOutEvent();
            output.Volume = ClampVolume(_volume);
            output.PlaybackStopped += OnPlaybackStopped;
            output.Init(reader);
            lock (_gate) _output = output;
            output.Play();
        }
        catch (Exception ex)
        {
            MusicPlayerViewModel.Instance.StatusText = "OGG 恢复失败：" + ex.Message;
        }
    }

    public void Stop()
    {
        StopInternal(raiseEnded: false);
    }

    private void StopInternal(bool raiseEnded)
    {
        WaveOutEvent? output;
        VorbisWaveReader? reader;
        lock (_gate)
        {
            output = _output;
            reader = _reader;
            _output = null;
            _reader = null;
            _pausedPosition = 0;
        }
        _manualStop = true;
        if (output is not null)
        {
            try { output.Stop(); } catch { }
            try { output.Dispose(); } catch { }
        }
        if (reader is not null)
        {
            try { reader.Dispose(); } catch { }
        }
        _manualStop = false;
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // 手动停止 / 释放设备也会触发本事件，用 _manualStop 过滤；只有自然播完才通知视图模型。
        if (_manualStop)
        {
            _manualStop = false;
            return;
        }
        RaiseEnded();
    }

    private void RaiseEnded()
    {
        // 启动极早期单例可能在 Application.Current 就绪前构造；OGG 播放只发生在用户交互后，
        // 此时 Application.Current 必然可用，故重新获取 UI 调度器（必要时落回调用线程）。
        var disp = _uiDispatcher ?? Application.Current?.Dispatcher;
        if (disp is not null && !disp.CheckAccess())
            disp.BeginInvoke(RaiseEnded);
        else
            Ended?.Invoke();
    }

    public void SetVolume(int volume)
    {
        _volume = volume;
        var v = ClampVolume(volume);
        lock (_gate)
        {
            if (_output is not null) _output.Volume = v;
        }
    }

    private static float ClampVolume(int volume) => (float)Math.Clamp(volume, 0, 100) / 100f;

    public double PositionSec
    {
        get
        {
            lock (_gate)
            {
                if (_output is not null && _reader is not null)
                    return _reader.CurrentTime.TotalSeconds;
                if (_output is null && _reader is not null)
                    return _pausedPosition; // 暂停态返回记忆位置
            }
            return 0;
        }
    }

    public double DurationSec
    {
        get
        {
            lock (_gate)
            {
                if (_reader is not null)
                    return _reader.TotalTime.TotalSeconds;
            }
            return 0;
        }
    }

    public void Seek(double seconds)
    {
        lock (_gate)
        {
            if (_reader is null) return;
            var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
            if (t > _reader.TotalTime) t = _reader.TotalTime;
            _reader.CurrentTime = t;
            if (_output is null) _pausedPosition = t.TotalSeconds;
        }
    }
}
