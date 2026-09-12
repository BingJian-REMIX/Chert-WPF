using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chert.Core.Statistics;

/// <summary>一次游玩会话。</summary>
public class PlaySession
{
    [JsonPropertyName("start")] public DateTime StartLocal { get; set; }
    [JsonPropertyName("end")] public DateTime EndLocal { get; set; }
    [JsonPropertyName("version")] public string VersionId { get; set; } = "";

    /// <summary>本次是否以崩溃结束。</summary>
    [JsonPropertyName("crashed")] public bool Crashed { get; set; }

    /// <summary>启动时加载的 Mod 数量（0 表示原版）。</summary>
    [JsonPropertyName("mods")] public int ModCount { get; set; }

    /// <summary>时长（分钟，最少 0）。</summary>
    [JsonIgnore]
    public double Minutes => Math.Max(0, (EndLocal - StartLocal).TotalMinutes);
}

/// <summary>
/// 会话流水（<c>mclcs_sessions.jsonl</c>，每行一条 JSON）。
/// 追加写入，避免整文件重写；年度报告在此之上聚合。
/// </summary>
public static class SessionLog
{
    public const string FileName = "mclcs_sessions.jsonl";

    public static string PathOf(string gameRoot) => Path.Combine(gameRoot, FileName);

    /// <summary>追加一条会话记录。</summary>
    public static bool Append(string gameRoot, PlaySession session)
    {
        try
        {
            Directory.CreateDirectory(gameRoot);
            File.AppendAllText(PathOf(gameRoot), JsonSerializer.Serialize(session) + Environment.NewLine);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>读取全部会话（损坏行自动跳过）。</summary>
    public static List<PlaySession> Load(string gameRoot)
    {
        var list = new List<PlaySession>();
        var path = PathOf(gameRoot);
        if (!File.Exists(path)) return list;

        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var s = JsonSerializer.Deserialize<PlaySession>(line);
                    if (s is not null) list.Add(s);
                }
                catch
                {
                    // 跳过损坏行
                }
            }
        }
        catch
        {
            // 读取失败返回已解析部分
        }
        return list;
    }

    /// <summary>清理指定年份之前的记录，返回删除条数。</summary>
    public static int PruneBefore(string gameRoot, int year)
    {
        var all = Load(gameRoot);
        var kept = all.Where(s => s.StartLocal.Year >= year).ToList();
        var removed = all.Count - kept.Count;
        if (removed <= 0) return 0;

        try
        {
            var sb = new StringBuilder();
            foreach (var s in kept) sb.AppendLine(JsonSerializer.Serialize(s));
            File.WriteAllText(PathOf(gameRoot), sb.ToString());
            return removed;
        }
        catch
        {
            return 0;
        }
    }
}

/// <summary>年度报告数据。</summary>
public class AnnualReportData
{
    public int Year { get; set; }
    public int SessionCount { get; set; }
    public double TotalMinutes { get; set; }

    /// <summary>有游玩记录的天数。</summary>
    public int ActiveDays { get; set; }

    /// <summary>最长的一次会话（分钟）。</summary>
    public double LongestSessionMinutes { get; set; }

    /// <summary>玩得最久的一天。</summary>
    public DateTime? BusiestDay { get; set; }
    public double BusiestDayMinutes { get; set; }

    /// <summary>各月时长（下标 0=1 月）。</summary>
    public double[] MonthlyMinutes { get; set; } = new double[12];

    /// <summary>各时段开局次数（下标 0=0 点）。</summary>
    public int[] HourHistogram { get; set; } = new int[24];

    /// <summary>星期分布（下标 0=周一）。</summary>
    public int[] WeekdayHistogram { get; set; } = new int[7];

    /// <summary>版本时长排行（降序）。</summary>
    public List<(string Version, double Minutes)> TopVersions { get; set; } = new();

    public int CrashCount { get; set; }

    /// <summary>崩溃率（0-1）。</summary>
    public double CrashRate => SessionCount == 0 ? 0 : (double)CrashCount / SessionCount;

    /// <summary>0 点—5 点的开局占比（"夜猫子指数"）。</summary>
    public double NightOwlRatio { get; set; }

    /// <summary>连续游玩的最长天数。</summary>
    public int LongestStreakDays { get; set; }

    public DateTime? FirstPlay { get; set; }
    public DateTime? LastPlay { get; set; }

    /// <summary>是否有足够数据生成报告。</summary>
    public bool HasData => SessionCount > 0;

    public double TotalHours => Math.Round(TotalMinutes / 60, 1);

    /// <summary>最活跃的月份（1-12），无数据返回 0。</summary>
    public int PeakMonth
    {
        get
        {
            var max = 0d;
            var idx = -1;
            for (var i = 0; i < 12; i++)
                if (MonthlyMinutes[i] > max) { max = MonthlyMinutes[i]; idx = i; }
            return idx < 0 ? 0 : idx + 1;
        }
    }

    /// <summary>最常开局的时段（0-23），无数据返回 -1。</summary>
    public int PeakHour
    {
        get
        {
            var max = 0;
            var idx = -1;
            for (var i = 0; i < 24; i++)
                if (HourHistogram[i] > max) { max = HourHistogram[i]; idx = i; }
            return idx;
        }
    }
}

/// <summary>
/// 年度报告（主页入口 / 全局功能）：把会话流水聚合成一份可展示、可导出的年度总结。
/// 聚合过程为纯函数，方便离线自检。
/// </summary>
public static class AnnualReport
{
    /// <summary>从会话列表生成指定年份的报告。</summary>
    public static AnnualReportData Generate(IEnumerable<PlaySession> sessions, int year)
    {
        var data = new AnnualReportData { Year = year };
        var list = sessions.Where(s => s.StartLocal.Year == year).OrderBy(s => s.StartLocal).ToList();
        if (list.Count == 0) return data;

        data.SessionCount = list.Count;
        data.TotalMinutes = Math.Round(list.Sum(s => s.Minutes), 1);
        data.CrashCount = list.Count(s => s.Crashed);
        data.FirstPlay = list[0].StartLocal;
        data.LastPlay = list[^1].EndLocal;
        data.LongestSessionMinutes = Math.Round(list.Max(s => s.Minutes), 1);

        foreach (var s in list)
        {
            data.MonthlyMinutes[s.StartLocal.Month - 1] += s.Minutes;
            data.HourHistogram[s.StartLocal.Hour]++;
            data.WeekdayHistogram[((int)s.StartLocal.DayOfWeek + 6) % 7]++;
        }
        for (var i = 0; i < 12; i++) data.MonthlyMinutes[i] = Math.Round(data.MonthlyMinutes[i], 1);

        var byDay = list.GroupBy(s => s.StartLocal.Date)
            .Select(g => (Day: g.Key, Minutes: g.Sum(x => x.Minutes)))
            .OrderByDescending(t => t.Minutes)
            .ToList();
        data.ActiveDays = byDay.Count;
        if (byDay.Count > 0)
        {
            data.BusiestDay = byDay[0].Day;
            data.BusiestDayMinutes = Math.Round(byDay[0].Minutes, 1);
        }

        data.TopVersions = list
            .Where(s => !string.IsNullOrWhiteSpace(s.VersionId))
            .GroupBy(s => s.VersionId)
            .Select(g => (Version: g.Key, Minutes: Math.Round(g.Sum(x => x.Minutes), 1)))
            .OrderByDescending(t => t.Minutes)
            .Take(5)
            .ToList();

        var night = list.Count(s => s.StartLocal.Hour is >= 0 and < 6);
        data.NightOwlRatio = Math.Round((double)night / list.Count, 3);
        data.LongestStreakDays = LongestStreak(byDay.Select(t => t.Day));

        return data;
    }

    /// <summary>从 <c>mclcs_sessions.jsonl</c> 生成报告。</summary>
    public static AnnualReportData GenerateFrom(string gameRoot, int year) =>
        Generate(SessionLog.Load(gameRoot), year);

    /// <summary>计算最长连续游玩天数。</summary>
    public static int LongestStreak(IEnumerable<DateTime> days)
    {
        var sorted = days.Select(d => d.Date).Distinct().OrderBy(d => d).ToList();
        if (sorted.Count == 0) return 0;

        var best = 1;
        var cur = 1;
        for (var i = 1; i < sorted.Count; i++)
        {
            if ((sorted[i] - sorted[i - 1]).TotalDays == 1) cur++;
            else cur = 1;
            if (cur > best) best = cur;
        }
        return best;
    }

    /// <summary>根据数据给出一句"年度称号"。</summary>
    public static string Title(AnnualReportData d)
    {
        if (!d.HasData) return "旁观者";
        if (d.NightOwlRatio >= 0.4) return "深夜矿工";
        if (d.LongestSessionMinutes >= 480) return "肝帝";
        if (d.LongestStreakDays >= 30) return "日更玩家";
        if (d.TopVersions.Count >= 4) return "版本游民";
        if (d.CrashRate >= 0.3) return "崩溃收藏家";
        if (d.TotalMinutes >= 6000) return "资深冒险家";
        return "稳健建造者";
    }

    // ---- 分享 Token（规格 3.13：年度报告支持导出 Token 分享）----

    /// <summary>Token 前缀，含版本位，方便以后升级格式而不误解析旧码。</summary>
    public const string TokenPrefix = "MCAR1.";

    /// <summary>Token 内部载荷（只带展示必需字段，让码尽量短）。</summary>
    private sealed class TokenPayload
    {
        [JsonPropertyName("y")] public int Year { get; set; }
        [JsonPropertyName("n")] public int SessionCount { get; set; }
        [JsonPropertyName("m")] public double TotalMinutes { get; set; }
        [JsonPropertyName("d")] public int ActiveDays { get; set; }
        [JsonPropertyName("l")] public double LongestSessionMinutes { get; set; }
        [JsonPropertyName("s")] public int LongestStreakDays { get; set; }
        [JsonPropertyName("c")] public int CrashCount { get; set; }
        [JsonPropertyName("o")] public double NightOwlRatio { get; set; }
        [JsonPropertyName("bd")] public long BusiestDayTicks { get; set; }
        [JsonPropertyName("bm")] public double BusiestDayMinutes { get; set; }
        [JsonPropertyName("mm")] public double[] MonthlyMinutes { get; set; } = new double[12];
        [JsonPropertyName("hh")] public int[] HourHistogram { get; set; } = new int[24];
        [JsonPropertyName("wd")] public int[] WeekdayHistogram { get; set; } = new int[7];

        /// <summary>版本排行，编码为 "版本|分钟"，避免元组序列化。</summary>
        [JsonPropertyName("tv")] public List<string> TopVersions { get; set; } = new();
    }

    /// <summary>
    /// 导出为一段可复制分享的短码：JSON → Deflate → Base64Url。
    /// 完全离线，接收方粘贴即可还原，不需要联网也不需要对方有同一份存档。
    /// </summary>
    public static string ExportToken(AnnualReportData d)
    {
        var payload = new TokenPayload
        {
            Year = d.Year,
            SessionCount = d.SessionCount,
            TotalMinutes = d.TotalMinutes,
            ActiveDays = d.ActiveDays,
            LongestSessionMinutes = d.LongestSessionMinutes,
            LongestStreakDays = d.LongestStreakDays,
            CrashCount = d.CrashCount,
            NightOwlRatio = d.NightOwlRatio,
            BusiestDayTicks = d.BusiestDay?.Ticks ?? 0,
            BusiestDayMinutes = d.BusiestDayMinutes,
            MonthlyMinutes = d.MonthlyMinutes,
            HourHistogram = d.HourHistogram,
            WeekdayHistogram = d.WeekdayHistogram,
            TopVersions = d.TopVersions.Select(t => $"{t.Version}|{t.Minutes}").ToList()
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(payload);

        using var ms = new MemoryStream();
        using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(json, 0, json.Length);

        return TokenPrefix + Base64UrlEncode(ms.ToArray());
    }

    /// <summary>解析分享 Token；格式错误或损坏返回 null（不抛异常）。</summary>
    public static AnnualReportData? ImportToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var t = token.Trim();
        if (!t.StartsWith(TokenPrefix, StringComparison.OrdinalIgnoreCase)) return null;

        try
        {
            var raw = Base64UrlDecode(t[TokenPrefix.Length..]);

            using var input = new MemoryStream(raw);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);

            var payload = JsonSerializer.Deserialize<TokenPayload>(output.ToArray());
            if (payload is null) return null;

            var d = new AnnualReportData
            {
                Year = payload.Year,
                SessionCount = payload.SessionCount,
                TotalMinutes = payload.TotalMinutes,
                ActiveDays = payload.ActiveDays,
                LongestSessionMinutes = payload.LongestSessionMinutes,
                LongestStreakDays = payload.LongestStreakDays,
                CrashCount = payload.CrashCount,
                NightOwlRatio = payload.NightOwlRatio,
                BusiestDayMinutes = payload.BusiestDayMinutes,
                BusiestDay = payload.BusiestDayTicks > 0 ? new DateTime(payload.BusiestDayTicks) : null
            };

            if (payload.MonthlyMinutes.Length == 12) d.MonthlyMinutes = payload.MonthlyMinutes;
            if (payload.HourHistogram.Length == 24) d.HourHistogram = payload.HourHistogram;
            if (payload.WeekdayHistogram.Length == 7) d.WeekdayHistogram = payload.WeekdayHistogram;

            foreach (var s in payload.TopVersions)
            {
                var i = s.LastIndexOf('|');
                if (i <= 0) continue;
                if (double.TryParse(s[(i + 1)..], out var min))
                    d.TopVersions.Add((s[..i], min));
            }

            return d;
        }
        catch
        {
            return null;
        }
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        var b = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b.PadRight(b.Length + (4 - b.Length % 4) % 4, '='));
    }

    /// <summary>渲染为 Markdown（可导出分享）。</summary>
    public static string RenderMarkdown(AnnualReportData d)
    {
        if (!d.HasData) return $"# {d.Year} 年度报告\n\n今年还没有游玩记录，去开一局吧。";

        var sb = new StringBuilder();
        sb.AppendLine($"# {d.Year} 年度报告").AppendLine();
        sb.AppendLine($"**年度称号：{Title(d)}**").AppendLine();
        sb.AppendLine($"- 总游玩：**{d.TotalHours} 小时**（{d.SessionCount} 次启动）");
        sb.AppendLine($"- 活跃天数：**{d.ActiveDays} 天**，最长连续 **{d.LongestStreakDays} 天**");
        sb.AppendLine($"- 单次最久：**{Math.Round(d.LongestSessionMinutes / 60, 1)} 小时**");
        if (d.BusiestDay is not null)
            sb.AppendLine($"- 最投入的一天：**{d.BusiestDay:yyyy-MM-dd}**（{Math.Round(d.BusiestDayMinutes / 60, 1)} 小时）");
        if (d.PeakMonth > 0)
            sb.AppendLine($"- 最活跃月份：**{d.PeakMonth} 月**");
        if (d.PeakHour >= 0)
            sb.AppendLine($"- 习惯开局时间：**{d.PeakHour}:00 前后**");
        sb.AppendLine($"- 崩溃 {d.CrashCount} 次（{d.CrashRate:P1}）");
        sb.AppendLine();

        if (d.TopVersions.Count > 0)
        {
            sb.AppendLine("## 版本排行").AppendLine();
            sb.AppendLine("| 版本 | 时长（小时） |");
            sb.AppendLine("| --- | --- |");
            foreach (var (ver, min) in d.TopVersions)
                sb.AppendLine($"| {ver} | {Math.Round(min / 60, 1)} |");
            sb.AppendLine();
        }

        sb.AppendLine("## 月度分布").AppendLine();
        var max = d.MonthlyMinutes.Max();
        for (var i = 0; i < 12; i++)
        {
            var bars = max <= 0 ? 0 : (int)Math.Round(d.MonthlyMinutes[i] / max * 20);
            sb.AppendLine($"{i + 1,2} 月 | {new string('█', bars)} {Math.Round(d.MonthlyMinutes[i] / 60, 1)}h");
        }

        return sb.ToString();
    }
}
