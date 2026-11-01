using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Chert.Core.Profiles;

namespace Chert.Core.Music;

/// <summary>
/// 厂家自建 API 的通用实现：路由与状态码由 <see cref="VendorApiProfile"/> 提供，
/// 本类只负责「按档案发请求 + 宽容解析」。
///
/// <para><b>为什么解析要写得这么「不讲究」</b>：同一厂家在不同版本间会改字段名，
/// 不同厂家之间更是毫无约定。写死 <c>result.songs[0].name</c> 这类路径，
/// 接口一变就是全线空结果；而按「找一个含 id 与名字的对象数组」去定位，
/// 只要接口还返回曲目列表就能读出来。代价是极端情况下可能定位错数组 ——
/// 但那也只是「多读/少读一层」，不会崩。</para>
///
/// <para><b>失败策略</b>：网络与解析异常收敛为 <see cref="MusicApiException"/>；
/// 歌词、头像这类装饰性数据失败一律返回 null。</para>
/// </summary>
public sealed class CommunityApiProvider : IOnlineMusicLoginProvider
{
    private readonly HttpClient _http;
    private readonly VendorApiProfile _profile;
    private readonly string _baseUrl;

    /// <summary>取播放地址时的 id 字段名（各厂家不同）。</summary>
    private static readonly string[] IdNames =
        { "id", "songId", "songid", "mid", "songmid", "hash", "rid", "audioId", "cid", "album_audio_id" };

    private static readonly string[] NameNames =
        { "name", "title", "songName", "songname", "fileName", "filename", "song_name", "audio_name" };

    public CommunityApiProvider(HttpClient http, VendorApiProfile profile, string baseUrl)
    {
        _http = http;
        _profile = profile;
        _baseUrl = (baseUrl ?? "").Trim().TrimEnd('/');
    }

    public string Name => _profile.DisplayName;

    public bool SupportsLogin => _profile.SupportsLogin;

    public bool SupportsPlaylistSearch => false;

    // ---- 请求 ----

    /// <summary>
    /// 拼完整 URL。档案里的路径模板可以自带 query（<c>/search?keywords={kw}</c>）。
    /// </summary>
    private string Url(string pathAndQuery)
    {
        if (string.IsNullOrWhiteSpace(_baseUrl))
            throw new MusicApiException($"未配置{_profile.DisplayName}的服务地址", isConfigurationError: true);

        var path = pathAndQuery.StartsWith('/') ? pathAndQuery : "/" + pathAndQuery;
        return _baseUrl + path;
    }

    /// <summary>把登录态附加到请求上（query 或 Cookie 头，取决于档案）。</summary>
    private static string AppendCookie(string pathAndQuery, string cookie, VendorCookieStyle style)
    {
        if (string.IsNullOrWhiteSpace(cookie) || style != VendorCookieStyle.Query) return pathAndQuery;
        var sep = pathAndQuery.Contains('?') ? '&' : '?';
        return pathAndQuery + sep + "cookie=" + Uri.EscapeDataString(cookie);
    }

    private async Task<JsonDocument> GetJsonAsync(string pathAndQuery, string cookie = "", CancellationToken ct = default)
    {
        var url = Url(AppendCookie(pathAndQuery, cookie, _profile.CookieStyle));

        string raw;
        try
        {
            if (_profile.CookieStyle == VendorCookieStyle.Header && !string.IsNullOrWhiteSpace(cookie))
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Cookie", cookie);
                using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
            else
            {
                raw = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MusicApiException(_profile.DisplayName + " 服务不可达：" + ex.Message);
        }

        try
        {
            return JsonDocument.Parse(raw);
        }
        catch (JsonException ex)
        {
            throw new MusicApiException(_profile.DisplayName + " 返回了无法解析的内容：" + ex.Message);
        }
    }

    // ---- 搜索 / 歌单 ----

    public async Task<IReadOnlyList<OnlineTrack>> SearchSongsAsync(string keyword, int limit = 30, CancellationToken ct = default)
    {
        if (!_profile.SupportsSearch || string.IsNullOrWhiteSpace(_profile.SearchPath)) return Array.Empty<OnlineTrack>();
        if (string.IsNullOrWhiteSpace(keyword)) return Array.Empty<OnlineTrack>();

        var path = VendorApiProfile.Expand(_profile.SearchPath,
            ("kw", keyword.Trim()), ("limit", Math.Clamp(limit, 1, 100).ToString(CultureInfo.InvariantCulture)));

        using var doc = await GetJsonAsync(path, ct: ct).ConfigureAwait(false);
        return ParseTracks(doc.RootElement);
    }

    public Task<IReadOnlyList<OnlinePlaylist>> SearchPlaylistsAsync(string keyword, int limit = 20, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OnlinePlaylist>>(Array.Empty<OnlinePlaylist>());

    public async Task<IReadOnlyList<OnlineTrack>> GetPlaylistTracksAsync(string playlistId, CancellationToken ct = default)
    {
        if (!_profile.SupportsPlaylistTracks || string.IsNullOrWhiteSpace(_profile.PlaylistDetailPath)
            || string.IsNullOrWhiteSpace(playlistId))
            return Array.Empty<OnlineTrack>();

        var path = VendorApiProfile.Expand(_profile.PlaylistDetailPath, ("id", playlistId));
        using var doc = await GetJsonAsync(path, ct: ct).ConfigureAwait(false);
        return ParseTracks(doc.RootElement);
    }

    public async Task<string?> GetPlayableUrlAsync(OnlineTrack track, MusicApiQuality quality,
        string cookie = "", CancellationToken ct = default)
    {
        if (track is null || string.IsNullOrWhiteSpace(track.Id) || string.IsNullOrWhiteSpace(_profile.UrlPath))
            return null;

        var path = VendorApiProfile.Expand(_profile.UrlPath,
            ("id", track.Id),
            ("quality", quality switch
            {
                MusicApiQuality.Standard => "standard",
                MusicApiQuality.Higher => "higher",
                MusicApiQuality.Lossless => "lossless",
                _ => "exhigh"
            }));

        try
        {
            using var doc = await GetJsonAsync(path, cookie, ct).ConfigureAwait(false);
            return MusicJson.FindUrl(doc.RootElement);
        }
        catch (MusicApiException) { return null; }
    }

    public async Task<string?> GetLyricAsync(OnlineTrack track, CancellationToken ct = default)
    {
        if (track is null || string.IsNullOrWhiteSpace(track.Id) || !_profile.SupportsLyric
            || string.IsNullOrWhiteSpace(_profile.LyricPath))
            return null;

        try
        {
            var path = VendorApiProfile.Expand(_profile.LyricPath, ("id", track.Id));
            using var doc = await GetJsonAsync(path, ct: ct).ConfigureAwait(false);
            return MusicJson.FindString(doc.RootElement, new[] { "lyric", "lrc", "lyrics", "content" });
        }
        catch (MusicApiException) { return null; }      // 歌词属装饰性，失败不打扰用户
    }

    // ---- 登录 ----

    public async Task<QrLoginSession> CreateQrLoginAsync(CancellationToken ct = default)
    {
        if (!_profile.SupportsLogin)
            throw new MusicApiException($"{_profile.DisplayName} 不支持扫码登录", isConfigurationError: true);

        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var stamp = _profile.QrKeyPath.Contains('?') ? "&timestamp=" + ts : "?timestamp=" + ts;

        string? key = null;
        string firstImage = "", firstText = "", keyAsContent = "";
        using (var kDoc = await GetJsonAsync(_profile.QrKeyPath + stamp, ct: ct).ConfigureAwait(false))
        {
            // id：QQMusicApi 等把会话标识叫 id；code 仅接受字符串型（数字型 code 是状态码，不是标识）
            key = MusicJson.FindString(kDoc.RootElement,
                new[] { "unikey", "key", "code", "qrcode_key", "qrcodeKey", "qrKey", "qrcode", "id" });
            if (!string.IsNullOrWhiteSpace(key) && key!.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                // ★ 有的实例直接把二维码内容塞在 key 位 —— 那不是「标识」，但它是待编码文本，别丢
                keyAsContent = key;
                key = null;
            }

            // 有些实例（QQMusicApi 等）在**同一次响应**里就带上了二维码素材，
            // 二次调用反而会拿到与 key 不匹配的新二维码 —— 先记下来备用。
            firstImage = MusicJson.FindString(kDoc.RootElement,
                new[] { "qrimg", "qrcodeImage", "image", "qrcodeBase64" }) ?? "";
            firstText = MusicJson.FindString(kDoc.RootElement,
                new[] { "qrurl", "qrUrl", "url", "qrcodeContent", "content" }) ?? "";
        }

        // 二次调用只在「create 路径与 key 路径不同」时进行；
        // 路径相同时（QQ 音乐）再调一次只会生成新二维码，与已取的 key 对不上。
        var keyPath = _profile.QrKeyPath.Split('?')[0].TrimEnd('/');
        var createBase = _profile.QrCreatePath.Split('?')[0].TrimEnd('/');
        var singleShot = string.Equals(keyPath, createBase, StringComparison.OrdinalIgnoreCase);

        string rawImage, rawText;
        if (singleShot)
        {
            rawImage = firstImage;
            rawText = firstText;
        }
        else
        {
            var createPath = string.IsNullOrWhiteSpace(key)
                ? _profile.QrCreatePath
                : VendorApiProfile.Expand(_profile.QrCreatePath, ("key", key!));

            using var qDoc = await GetJsonAsync(createPath, ct: ct).ConfigureAwait(false);
            var root = qDoc.RootElement;
            rawImage = MusicJson.FindString(root, new[] { "qrimg", "qrcodeImage", "image", "qrcodeBase64" }) ?? "";
            rawText = MusicJson.FindString(root, new[] { "qrurl", "qrUrl", "url", "qrcodeContent", "content" }) ?? "";

            // 首次响应里若有素材而二次调用没拿到，回填首次的（兼容只生成一次的实例）
            if (string.IsNullOrEmpty(rawImage)) rawImage = firstImage;
            if (string.IsNullOrEmpty(rawText)) rawText = firstText;
        }

        // 归一成两路：
        //   图片（data URL 或 http(s) 图片地址）→ QrDataUrl，界面直接显示；
        //   其余文本（登录链接等）→ QrContent，界面本地生成二维码。
        // 此前 http 图片地址被丢弃、纯文本没人消费，QQ 音乐等于是「扫码框全黑」。
        string qrImage = "", qrContent = "";
        void Accept(string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) return;
            var isImage = v.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                          || LooksLikeImageUrl(v);
            if (isImage)
            {
                if (string.IsNullOrEmpty(qrImage)) qrImage = v!;
                else if (string.IsNullOrEmpty(qrContent)) qrContent = v!;
            }
            else
            {
                if (string.IsNullOrEmpty(qrContent)) qrContent = v!;
                else if (string.IsNullOrEmpty(qrImage)) qrImage = v!;
            }
        }
        Accept(rawImage);
        Accept(rawText);
        Accept(keyAsContent);   // key 位被当二维码内容的，最后捡回来

        return new QrLoginSession
        {
            Key = key ?? "",
            QrDataUrl = qrImage,
            QrContent = qrContent,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };
    }

    /// <summary>判断字符串是否像图片地址（而非待编码的二维码文本）。</summary>
    private static bool LooksLikeImageUrl(string s)
    {
        if (!s.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var path = new Uri(s).AbsolutePath;
            return path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public async Task<QrLoginPollResult> PollQrLoginAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_profile.QrCheckPath))
            return new QrLoginPollResult { State = QrLoginState.Failed, Message = "该厂家未配置扫码状态查询路由" };

        var path = VendorApiProfile.Expand(_profile.QrCheckPath, ("key", key ?? ""));
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        path += (path.Contains('?') ? "&timestamp=" : "?timestamp=") + ts;

        using var doc = await GetJsonAsync(path, ct: ct).ConfigureAwait(false);
        var root = doc.RootElement;

        return _profile.QrStyle switch
        {
            VendorQrStyle.Kugou => MapKugou(root),
            VendorQrStyle.Tencent => MapTencent(root),
            _ => MapNetease(root)
        };
    }

    /// <summary>网易云：code 800 过期 / 801 待扫 / 802 待确认 / 803 成功。</summary>
    private static QrLoginPollResult MapNetease(JsonElement root)
    {
        var code = MusicJson.Int(root, "code");
        return code switch
        {
            800 => new QrLoginPollResult { State = QrLoginState.Expired, Message = "二维码已过期，请重新获取" },
            801 => new QrLoginPollResult { State = QrLoginState.Pending, Message = "等待扫码" },
            802 => new QrLoginPollResult { State = QrLoginState.Scanned, Message = "已扫描，请在手机上确认" },
            803 => new QrLoginPollResult
            {
                State = QrLoginState.Confirmed,
                Cookie = ExtractCookie(root),
                Message = "登录成功"
            },
            _ => new QrLoginPollResult
            {
                State = QrLoginState.Failed,
                Message = MusicJson.Str(root, "message") ?? $"未知状态（code={code}）"
            }
        };
    }

    /// <summary>酷狗：data.status 为 0 过期 / 1 待扫 / 2 待确认 / 4 授权成功。</summary>
    private static QrLoginPollResult MapKugou(JsonElement root)
    {
        // 状态可能直接挂在根上，也可能裹在 data 里 —— 两种都读
        var status = root.TryGetProperty("data", out var data) ? MusicJson.Int(data, "status") : -1;
        if (status < 0) status = MusicJson.Int(root, "status");
        if (status < 0) status = MusicJson.Int(root, "code");

        return status switch
        {
            0 => new QrLoginPollResult { State = QrLoginState.Expired, Message = "二维码已过期，请重新获取" },
            1 => new QrLoginPollResult { State = QrLoginState.Pending, Message = "等待扫码" },
            2 => new QrLoginPollResult { State = QrLoginState.Scanned, Message = "已扫描，请在手机上确认" },
            4 => new QrLoginPollResult
            {
                State = QrLoginState.Confirmed,
                Cookie = ExtractCookie(root),
                Message = "登录成功"
            },
            _ => new QrLoginPollResult
            {
                State = QrLoginState.Failed,
                Message = MusicJson.Str(root, "message") ?? $"未知状态（status={status}）"
            }
        };
    }

    /// <summary>
    /// QQ 音乐：没有公开的状态码约定，因此以「响应里是否带了凭证」为准 ——
    /// 凭证出现即视为登录成功，其余按常见状态词与数字猜，猜不出就报 Pending 让上层继续轮询
    /// （比贸然判失败导致流程中断体面）。
    /// </summary>
    private static QrLoginPollResult MapTencent(JsonElement root)
    {
        var cookie = ExtractCookie(root);
        if (!string.IsNullOrWhiteSpace(cookie))
            return new QrLoginPollResult { State = QrLoginState.Confirmed, Cookie = cookie, Message = "登录成功" };

        var text = (MusicJson.Str(root, "status") ?? MusicJson.Str(root, "state") ?? "").Trim().ToLowerInvariant();
        var code = MusicJson.Int(root, "code");

        if (text.Contains("expire") || code == 800 || code == 2)
            return new QrLoginPollResult { State = QrLoginState.Expired, Message = "二维码已过期，请重新获取" };
        if (text.Contains("confirm") || text.Contains("scanned") || text.Contains("wait_confirm") || code == 1)
            return new QrLoginPollResult { State = QrLoginState.Scanned, Message = "已扫描，请在手机上确认" };
        if (text.Contains("error") || text.Contains("fail"))
            return new QrLoginPollResult { State = QrLoginState.Failed, Message = text };

        return new QrLoginPollResult { State = QrLoginState.Pending, Message = "等待扫码" };
    }

    /// <summary>从响应里抽登录凭证：cookie 串，或者 token/userid 两个字段拼成 cookie 串。</summary>
    private static string ExtractCookie(JsonElement root)
    {
        var cookie = MusicJson.FindString(root, new[] { "cookie", "set-cookie" });
        if (!string.IsNullOrWhiteSpace(cookie)) return cookie!;

        var token = MusicJson.FindString(root, new[] { "token", "musickey", "musicKey" });
        var userId = MusicJson.FindString(root, new[] { "userid", "userId", "uin", "musicid", "musicId" });

        if (string.IsNullOrWhiteSpace(token) && string.IsNullOrWhiteSpace(userId)) return "";

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(token)) parts.Add("token=" + token);
        if (!string.IsNullOrWhiteSpace(userId)) parts.Add("userid=" + userId);
        return string.Join("; ", parts);
    }

    public async Task<OnlineAccount?> GetAccountAsync(string cookie, CancellationToken ct = default)
    {
        if (!_profile.SupportsAccount || string.IsNullOrWhiteSpace(_profile.AccountPath)
            || string.IsNullOrWhiteSpace(cookie))
            return null;

        try
        {
            using var doc = await GetJsonAsync(_profile.AccountPath, cookie, ct).ConfigureAwait(false);
            var root = doc.RootElement;

            var nickname = MusicJson.FindString(root, new[] { "nickname", "nickName", "userName", "user_name", "name" });
            if (string.IsNullOrWhiteSpace(nickname)) return null;

            return new OnlineAccount
            {
                UserId = MusicJson.FindString(root, new[] { "userId", "userid", "uid", "uin", "id" }) ?? "",
                Nickname = nickname!,
                AvatarUrl = MusicJson.FindString(root, new[] { "avatarUrl", "avatar", "picUrl", "headImg", "user_pic" }) ?? ""
            };
        }
        catch (MusicApiException) { return null; }
    }

    public async Task<IReadOnlyList<OnlinePlaylist>> GetMyPlaylistsAsync(string cookie, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_profile.MyPlaylistsPath) || string.IsNullOrWhiteSpace(cookie))
            return Array.Empty<OnlinePlaylist>();

        // 需要 uid 的厂家（QQ 音乐）先从凭证里取，取不到再用账户接口
        var uid = "";
        foreach (var part in cookie.Split(';'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Trim().Equals("userid", StringComparison.OrdinalIgnoreCase))
                uid = kv[1].Trim();
        }
        if (string.IsNullOrWhiteSpace(uid))
            uid = (await GetAccountAsync(cookie, ct).ConfigureAwait(false))?.UserId ?? "";

        var path = VendorApiProfile.Expand(_profile.MyPlaylistsPath, ("uid", uid));

        using var doc = await GetJsonAsync(path, cookie, ct).ConfigureAwait(false);
        return ParsePlaylists(doc.RootElement, mine: true);
    }

    // ---- 解析 ----

    private IReadOnlyList<OnlineTrack> ParseTracks(JsonElement root)
    {
        var arr = MusicJson.FindRecordArray(root, IdNames, NameNames);
        if (arr is null) return Array.Empty<OnlineTrack>();

        var list = new List<OnlineTrack>();
        foreach (var e in arr.Value.EnumerateArray())
        {
            var t = ParseTrack(e);
            if (t is not null) list.Add(t);
        }
        return list;
    }

    private IReadOnlyList<OnlinePlaylist> ParsePlaylists(JsonElement root, bool mine)
    {
        var arr = MusicJson.FindRecordArray(root, IdNames, NameNames);
        if (arr is null) return Array.Empty<OnlinePlaylist>();

        var list = new List<OnlinePlaylist>();
        foreach (var p in arr.Value.EnumerateArray())
        {
            var pl = ParsePlaylist(p, mine);
            if (pl is not null) list.Add(pl);
        }
        return list;
    }

    /// <summary>
    /// 解析一首歌。字段名按「各厂家候选」依次试探 —— 曲目标识在网易云叫 id、
    /// 在 QQ 音乐叫 mid、在酷狗叫 hash；时长有的给毫秒有的给秒（&gt;100 分钟必是毫秒）。
    /// </summary>
    private OnlineTrack? ParseTrack(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;

        var id = MusicJson.Any(e, IdNames);
        if (string.IsNullOrWhiteSpace(id)) return null;

        var title = MusicJson.Any(e, NameNames) ?? "";
        var artist = MusicJson.Names(e, "artists")
                     ?? MusicJson.Names(e, "artist")
                     ?? MusicJson.Names(e, "ar")
                     ?? MusicJson.Names(e, "authors")
                     ?? MusicJson.Any(e, "author", "singername", "singerName", "singer") ?? "";

        var albumEl = e.TryGetProperty("album", out var album) ? album
                    : e.TryGetProperty("al", out var al) ? al
                    : (JsonElement?)null;
        var albumName = albumEl is { } a ? (MusicJson.Any(a, "name", "albumName") ?? "") : "";

        var duration = MusicJson.Dbl(e, "duration");
        if (duration <= 0) duration = MusicJson.Dbl(e, "dt");
        if (duration <= 0) duration = MusicJson.Dbl(e, "interval");
        if (duration <= 0) duration = MusicJson.Dbl(e, "timelen");
        if (duration > 6000) duration /= 1000.0;      // 毫秒

        return new OnlineTrack
        {
            Id = id!,
            Platform = _profile.Platform,
            Title = title,
            Artist = artist,
            Album = albumName,
            DurationSec = duration,
            CoverUrl = MusicJson.Any(e, "pic", "picUrl", "img", "cover", "albumPic", "albumpic", "image") ?? ""
        };
    }

    /// <summary>曲目数：各厂家字段名不一（trackCount / total / songnum）。</summary>
    private static int TrackCountOf(JsonElement p)
    {
        foreach (var name in new[] { "trackCount", "total", "songnum", "songNum", "count" })
        {
            var n = MusicJson.Int(p, name);
            if (n > 0) return n;
        }
        return 0;
    }

    private OnlinePlaylist? ParsePlaylist(JsonElement p, bool mine)
    {
        if (p.ValueKind != JsonValueKind.Object) return null;

        var id = MusicJson.Any(p, IdNames);
        if (string.IsNullOrWhiteSpace(id)) return null;

        var creator = "";
        if (p.TryGetProperty("creator", out var c) && c.ValueKind == JsonValueKind.Object)
            creator = MusicJson.Any(c, "nickname", "nickName", "userName") ?? "";
        if (string.IsNullOrWhiteSpace(creator))
            creator = MusicJson.Any(p, "creator", "userName", "nickname") ?? "";

        return new OnlinePlaylist
        {
            Id = id!,
            Platform = _profile.Platform,
            Name = MusicJson.Any(p, NameNames) ?? "",
            Creator = creator,
            TrackCount = TrackCountOf(p),
            CoverUrl = MusicJson.Any(p, "coverImgUrl", "picUrl", "pic", "cover", "img") ?? "",
            IsMine = mine
        };
    }
}
