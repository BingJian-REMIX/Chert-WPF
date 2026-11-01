using System.Text.Json;

namespace Chert.Core.Statistics;

/// <summary>启动器统计信息（主页统计区 / 设置展示）。</summary>
public class PlayStats
{
    public long TotalPlayMinutes { get; set; }
    public long WeeklyPlayMinutes { get; set; }
    public string? WeekStartIso { get; set; }
    public int LaunchCount { get; set; }
    public int CrashCount { get; set; }
    public string? RecentVersion { get; set; }
    public string? LastPlayedUtc { get; set; }

    /// <summary>首次通过启动器启动游戏的 UTC 时间（用于年度报告周年日入口）。</summary>
    public string? FirstLaunchUtc { get; set; }

    /// <summary>
    /// 未收口的游玩会话（issue #5-⑤）：游戏已启动但还没走到退出统计时，
    /// 启动器中途退出/崩溃也能在下次启动补记时长。按 pid 匹配，天然支持多开。
    /// </summary>
    public List<OpenSession> OpenSessions { get; set; } = new();
}

/// <summary>一次尚未收口的游玩会话。</summary>
public class OpenSession
{
    public string StartIso { get; set; } = "";
    public string VersionId { get; set; } = "";
    public int ModCount { get; set; }
    public int Pid { get; set; }
}

/// <summary>
/// 游玩统计（主页统计区 / 全局功能）：记录启动次数、累计与本周游玩时长、崩溃次数、
/// 最近游玩版本，持久化到 <c>mclcs_stats.json</c>。
/// </summary>
public static class PlaytimeTracker
{
    private static string Path(string gameRoot) => System.IO.Path.Combine(gameRoot, "mclcs_stats.json");

    public static PlayStats Load(string gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot)) return new PlayStats();
        try
        {
            var p = Path(gameRoot);
            if (!File.Exists(p)) return new PlayStats();
            return JsonSerializer.Deserialize<PlayStats>(File.ReadAllText(p)) ?? new();
        }
        catch { return new PlayStats(); }
    }

    private static void Save(string gameRoot, PlayStats s)
    {
        Directory.CreateDirectory(gameRoot);
        File.WriteAllText(Path(gameRoot),
            JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// 一次启动开始时调用：记录最近版本、累计启动次数，并登记未收口会话。
    /// <paramref name="pid"/> 为游戏进程 id（&gt;0 时登记，用于退出时匹配 / 崩溃后补记）。
    /// </summary>
    public static PlayStats RecordLaunch(string gameRoot, string versionId, int modCount = 0, int pid = 0)
    {
        var s = Load(gameRoot);
        ReconcileOpenSessions(gameRoot, s);
        s.LaunchCount++;
        if (string.IsNullOrWhiteSpace(s.FirstLaunchUtc))
            s.FirstLaunchUtc = DateTime.UtcNow.ToString("o");
        s.RecentVersion = versionId;
        s.LastPlayedUtc = DateTime.UtcNow.ToString("o");
        EnsureWeek(ref s);
        if (pid > 0)
        {
            s.OpenSessions.Add(new OpenSession
            {
                StartIso = DateTime.Now.ToString("o"),
                VersionId = versionId,
                ModCount = modCount,
                Pid = pid
            });
        }
        Save(gameRoot, s);
        return s;
    }

    /// <summary>
    /// 一次游玩结束时调用（issue #5-⑤）：按会话起止时间累加时长、
    /// 落会话流水（年度报告数据源）、必要时记崩溃。
    /// </summary>
    public static PlayStats RecordSessionEnd(string gameRoot, int pid, bool crashed,
        string? versionId = null, int modCount = 0)
    {
        var s = Load(gameRoot);
        EnsureWeek(ref s);

        var rec = pid > 0 ? s.OpenSessions.FirstOrDefault(o => o.Pid == pid) : null;
        if (rec is not null) s.OpenSessions.Remove(rec);

        var start = rec is not null
            && DateTime.TryParse(rec.StartIso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var st)
                ? st : DateTime.Now;
        var end = DateTime.Now;
        if (end < start) end = start;
        var minutes = (long)Math.Round((end - start).TotalMinutes);
        if (minutes < 0) minutes = 0;

        s.TotalPlayMinutes += minutes;
        s.WeeklyPlayMinutes += minutes;
        if (crashed) s.CrashCount++;

        SessionLog.Append(gameRoot, new PlaySession
        {
            StartLocal = start,
            EndLocal = end,
            VersionId = rec?.VersionId ?? versionId ?? "",
            Crashed = crashed,
            ModCount = rec?.ModCount ?? modCount
        });
        Save(gameRoot, s);
        return s;
    }

    /// <summary>补记所有未收口会话（启动器启动时调用；进程仍存活的不动）。</summary>
    public static void ReconcileOpenSessions(string gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot)) return;
        var s = Load(gameRoot);
        ReconcileOpenSessions(gameRoot, s);
        Save(gameRoot, s);
    }

    private static void ReconcileOpenSessions(string gameRoot, PlayStats s)
    {
        if (s.OpenSessions.Count == 0) return;

        var dead = s.OpenSessions.Where(o => !IsPidAlive(o.Pid)).ToList();
        foreach (var rec in dead)
        {
            s.OpenSessions.Remove(rec);
            if (!DateTime.TryParse(rec.StartIso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var start))
                continue;

            var end = DateTime.Now;
            // 上限 12 小时：启动器崩溃后隔天才补记，不能把离线时间全算成游玩
            if ((end - start).TotalHours > 12) end = start.AddHours(12);
            if (end < start) end = start;
            var minutes = (long)Math.Round((end - start).TotalMinutes);
            if (minutes < 0) minutes = 0;

            s.TotalPlayMinutes += minutes;
            s.WeeklyPlayMinutes += minutes;
            SessionLog.Append(gameRoot, new PlaySession
            {
                StartLocal = start,
                EndLocal = end,
                VersionId = rec.VersionId,
                Crashed = false,          // 启动器不在场时无法判定崩溃
                ModCount = rec.ModCount
            });
        }
    }

    private static bool IsPidAlive(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>一次游玩结束时调用：累加时长（分钟）。</summary>
    public static PlayStats RecordPlayMinutes(string gameRoot, long minutes)
    {
        var s = Load(gameRoot);
        EnsureWeek(ref s);
        s.TotalPlayMinutes += minutes;
        s.WeeklyPlayMinutes += minutes;
        Save(gameRoot, s);
        return s;
    }

    /// <summary>记录一次崩溃。</summary>
    public static PlayStats RecordCrash(string gameRoot)
    {
        var s = Load(gameRoot);
        s.CrashCount++;
        Save(gameRoot, s);
        return s;
    }

    private static void EnsureWeek(ref PlayStats s)
    {
        var thisMonday = MondayOf(DateTime.UtcNow);
        var iso = thisMonday.ToString("o");
        if (s.WeekStartIso != iso)
        {
            s.WeekStartIso = iso;
            s.WeeklyPlayMinutes = 0;
        }
    }

    private static DateTime MondayOf(DateTime utc)
    {
        var diff = (int)utc.DayOfWeek - (int)DayOfWeek.Monday;
        if (diff < 0) diff += 7;
        return utc.Date.AddDays(-diff);
    }
}
