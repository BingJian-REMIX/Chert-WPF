using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Chert.App.Themes;

/// <summary>
/// 清单 #28：节日音频（BGM / 按钮点击音效）。
/// <para>
/// 音频文件<b>不内置于程序</b>，由远程 <c>config.json</c> 的 <c>audio</c> 段给出直链，
/// 首次使用时下载到 <c>&lt;游戏目录&gt;/seasonal_audio/</c> 缓存。
/// 没有配置 / 下载失败 / 用户关闭节日特效时全程静默，绝不影响正常使用。
/// </para>
/// <para>
/// 点击音效通过 <see cref="System.Windows.EventManager"/> 注册到 <c>Button</c> 类级别，
/// 无需逐个控件挂钩即可覆盖全应用按钮。
/// </para>
/// </summary>
public static class SeasonalAudioService
{
    private static MediaPlayer? _bgm;
    private static MediaPlayer? _sfx;
    private static string _sfxPath = "";
    private static bool _installed;
    private static bool _enabled = true;

    /// <summary>总开关（跟随「关闭节日特效」设置）。置 false 立即停止 BGM 与音效。</summary>
    public static bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value)
            {
                StopBgm();
                _sfxPath = "";
            }
        }
    }

    /// <summary>注册按钮点击音效的类级处理器（幂等，可在启动时安全调用一次）。</summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        try
        {
            EventManager.RegisterClassHandler(typeof(Button), ButtonBase.ClickEvent,
                new RoutedEventHandler((_, _) => PlayClick()));
        }
        catch
        {
            // 注册失败不影响任何功能
        }
    }

    /// <summary>播放一次点击音效；无音效文件时为空操作。</summary>
    public static void PlayClick()
    {
        if (!_enabled || string.IsNullOrEmpty(_sfxPath) || !File.Exists(_sfxPath)) return;
        try
        {
            _sfx ??= new MediaPlayer();
            _sfx.Open(new Uri(_sfxPath, UriKind.Absolute));
            _sfx.Play();
        }
        catch
        {
            // 播放失败静默
        }
    }

    /// <summary>按当前生效的节日配置启用音频。</summary>
    public static void Apply(HolidayConfig config, string gameRoot)
    {
        try
        {
            StopBgm();
            _sfxPath = "";

            var audio = config.Audio;
            if (audio is null || !_enabled) return;

            _ = LoadAsync(audio, gameRoot);
        }
        catch
        {
            // 非关键功能
        }
    }

    private static async Task LoadAsync(SeasonalAudioConfig cfg, string gameRoot)
    {
        try
        {
            var dir = Path.Combine(gameRoot, "seasonal_audio");
            Directory.CreateDirectory(dir);

            if (!string.IsNullOrWhiteSpace(cfg.Click))
            {
                var path = await FetchAsync(cfg.Click, dir).ConfigureAwait(false);
                if (path is not null) _sfxPath = path;
            }

            if (!string.IsNullOrWhiteSpace(cfg.Bgm))
            {
                var path = await FetchAsync(cfg.Bgm, dir).ConfigureAwait(false);
                if (path is not null) StartBgm(path, cfg.Volume);
            }
        }
        catch
        {
            // 下载 / 缓存失败静默
        }
    }

    private static void StartBgm(string path, double volume)
    {
        var app = Application.Current;
        if (app is null) return;

        void Act()
        {
            try
            {
                StopBgmCore();
                _bgm = new MediaPlayer();
                _bgm.Open(new Uri(path, UriKind.Absolute));
                _bgm.Volume = Math.Clamp(volume <= 0 ? 0.35 : volume, 0, 1);
                _bgm.MediaEnded += (_, _) =>
                {
                    try
                    {
                        _bgm.Position = TimeSpan.Zero;
                        _bgm.Play();
                    }
                    catch { /* 循环失败即停止 */ }
                };
                _bgm.Play();
            }
            catch
            {
                // 播放失败静默
            }
        }

        try
        {
            if (app.Dispatcher.CheckAccess()) Act();
            else app.Dispatcher.Invoke(Act);
        }
        catch
        {
            // 调度失败静默
        }
    }

    private static void StopBgm()
    {
        var app = Application.Current;
        if (app is null) { StopBgmCore(); return; }

        try
        {
            if (app.Dispatcher.CheckAccess()) StopBgmCore();
            else app.Dispatcher.Invoke(StopBgmCore);
        }
        catch
        {
            // 忽略
        }
    }

    private static void StopBgmCore()
    {
        try
        {
            if (_bgm is null) return;
            _bgm.Stop();
            _bgm.Close();
            _bgm = null;
        }
        catch
        {
            _bgm = null;
        }
    }

    /// <summary>按 URL 下载并缓存音频，返回本地路径；失败返回 null。</summary>
    private static async Task<string?> FetchAsync(string url, string dir)
    {
        try
        {
            var name = Sha1Hex(url) + GuessExtension(url);
            var path = Path.Combine(dir, name);
            if (File.Exists(path) && new FileInfo(path).Length > 0) return path;

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var bytes = await client.GetByteArrayAsync(url).ConfigureAwait(false);
            if (bytes.Length == 0) return null;

            await File.WriteAllBytesAsync(path + ".tmp", bytes).ConfigureAwait(false);
            File.Move(path + ".tmp", path, overwrite: true);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static string GuessExtension(string url)
    {
        var i = url.LastIndexOf('.');
        if (i < 0) return ".mp3";
        var ext = url[i..];
        if (ext.Length > 5) return ".mp3";
        // 去掉可能的查询串
        var q = ext.IndexOf('?');
        if (q >= 0) ext = ext[..q];
        return ext;
    }

    private static string Sha1Hex(string s)
    {
        using var sha = SHA1.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
    }
}
