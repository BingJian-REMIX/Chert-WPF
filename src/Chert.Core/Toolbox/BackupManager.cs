using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chert.Core.Toolbox;

/// <summary>备份类型。</summary>
public enum BackupKind
{
    /// <summary>单个存档。</summary>
    Save,
    /// <summary>配置目录（config/、options.txt 等）。</summary>
    Config,
    /// <summary>Mod 目录。</summary>
    Mods,
    /// <summary>自定义目录。</summary>
    Custom
}

/// <summary>一条备份记录。</summary>
public class BackupRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("kind")] public BackupKind Kind { get; set; }

    /// <summary>备份来源（存档名 / 目录名）。</summary>
    [JsonPropertyName("sourceName")] public string SourceName { get; set; } = "";

    /// <summary>备份 zip 的完整路径。</summary>
    [JsonPropertyName("archivePath")] public string ArchivePath { get; set; } = "";

    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; } = DateTime.Now;
    [JsonPropertyName("sizeBytes")] public long SizeBytes { get; set; }
    [JsonPropertyName("fileCount")] public int FileCount { get; set; }

    /// <summary>用户备注。</summary>
    [JsonPropertyName("note")] public string? Note { get; set; }

    /// <summary>是否为自动备份（启动前自动创建）。</summary>
    [JsonPropertyName("auto")] public bool Auto { get; set; }

    public string SizeText => SizeBytes switch
    {
        < 1024 => $"{SizeBytes} B",
        < 1024 * 1024 => $"{SizeBytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{SizeBytes / 1024.0 / 1024:F1} MB",
        _ => $"{SizeBytes / 1024.0 / 1024 / 1024:F2} GB"
    };

    public bool Exists => !string.IsNullOrEmpty(ArchivePath) && File.Exists(ArchivePath);
}

/// <summary>定时备份频率。</summary>
public enum BackupSchedule
{
    /// <summary>不定时。</summary>
    Off,
    /// <summary>每天。</summary>
    Daily,
    /// <summary>每周。</summary>
    Weekly,
    /// <summary>每月。</summary>
    Monthly
}

/// <summary>备份保留策略。</summary>
public class BackupPolicy
{
    /// <summary>每个来源最多保留多少份（0 = 不限）。</summary>
    [JsonPropertyName("keepPerSource")] public int KeepPerSource { get; set; } = 5;

    /// <summary>超过多少天的自动备份会被清理（0 = 不按时间清理）。</summary>
    [JsonPropertyName("maxAgeDays")] public int MaxAgeDays { get; set; } = 30;

    /// <summary>启动游戏前自动备份当前存档。</summary>
    [JsonPropertyName("autoBeforeLaunch")] public bool AutoBeforeLaunch { get; set; }

    /// <summary>
    /// 备份总目录。相对路径按 gameRoot 解析；也可填绝对路径（如移动硬盘）。
    /// </summary>
    [JsonPropertyName("folder")] public string Folder { get; set; } = "backups";

    /// <summary>定时备份频率。</summary>
    [JsonPropertyName("schedule")] public BackupSchedule Schedule { get; set; } = BackupSchedule.Off;

    /// <summary>上次定时备份的时间（本地时间；null 表示从未执行）。</summary>
    [JsonPropertyName("lastScheduledRun")] public DateTime? LastScheduledRun { get; set; }

    /// <summary>恢复备份前先自动备份当前状态（规格要求，默认开）。</summary>
    [JsonPropertyName("backupBeforeRestore")] public bool BackupBeforeRestore { get; set; } = true;
}

/// <summary>备份操作结果。</summary>
public sealed class BackupResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public BackupRecord? Record { get; init; }

    public static BackupResult Fail(string error) => new() { Ok = false, Error = error };
}

/// <summary>
/// 备份管理器（工具箱面板 11）：把存档 / 配置 / Mod 目录打成 zip，支持列出、恢复、按策略清理。
/// 索引文件为 <c>{gameRoot}/{folder}/backups.json</c>。
/// </summary>
public static class BackupManager
{
    public const string IndexFileName = "backups.json";

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>
    /// 解析备份根目录。<see cref="BackupPolicy.Folder"/> 为绝对路径时直接采用
    /// （支持备份到移动硬盘 / 其他分区），否则相对 <paramref name="gameRoot"/>。
    /// </summary>
    public static string BackupRoot(string gameRoot, BackupPolicy? policy = null)
    {
        var folder = (policy ?? new BackupPolicy()).Folder;
        if (string.IsNullOrWhiteSpace(folder)) folder = "backups";
        return Path.IsPathRooted(folder) ? folder : Path.Combine(gameRoot, folder);
    }

    public static string IndexPath(string gameRoot, BackupPolicy? policy = null) =>
        Path.Combine(BackupRoot(gameRoot, policy), IndexFileName);

    /// <summary>生成备份文件名：<c>{kind}_{source}_{yyyyMMdd-HHmmss}.zip</c>。</summary>
    public static string BuildArchiveName(BackupKind kind, string sourceName, DateTime time)
    {
        var safe = sourceName;
        foreach (var c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
        if (safe.Length > 40) safe = safe[..40];
        return $"{kind.ToString().ToLowerInvariant()}_{safe}_{time:yyyyMMdd-HHmmss}.zip";
    }

    /// <summary>读取备份索引（不存在返回空表）。</summary>
    public static List<BackupRecord> LoadIndex(string gameRoot, BackupPolicy? policy = null)
    {
        try
        {
            var path = IndexPath(gameRoot, policy);
            if (!File.Exists(path)) return new List<BackupRecord>();
            return JsonSerializer.Deserialize<List<BackupRecord>>(File.ReadAllText(path))
                   ?? new List<BackupRecord>();
        }
        catch
        {
            return new List<BackupRecord>();
        }
    }

    /// <summary>写入备份索引。</summary>
    public static bool SaveIndex(string gameRoot, List<BackupRecord> records, BackupPolicy? policy = null)
    {
        try
        {
            var root = BackupRoot(gameRoot, policy);
            Directory.CreateDirectory(root);
            File.WriteAllText(IndexPath(gameRoot, policy), JsonSerializer.Serialize(records, JsonOpts));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>创建一份备份。</summary>
    public static BackupResult Create(
        string gameRoot, string sourceDir, BackupKind kind,
        string? note = null, bool auto = false, BackupPolicy? policy = null)
    {
        policy ??= new BackupPolicy();

        if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
            return BackupResult.Fail("源目录不存在");

        try
        {
            var root = BackupRoot(gameRoot, policy);
            Directory.CreateDirectory(root);

            var sourceName = new DirectoryInfo(sourceDir).Name;
            var now = DateTime.Now;
            var archivePath = Path.Combine(root, BuildArchiveName(kind, sourceName, now));

            var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
            if (File.Exists(archivePath)) File.Delete(archivePath);
            ZipFile.CreateFromDirectory(sourceDir, archivePath, CompressionLevel.Fastest, includeBaseDirectory: false);

            var record = new BackupRecord
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                Kind = kind,
                SourceName = sourceName,
                ArchivePath = archivePath,
                CreatedAt = now,
                SizeBytes = new FileInfo(archivePath).Length,
                FileCount = files.Length,
                Note = note,
                Auto = auto
            };

            var index = LoadIndex(gameRoot, policy);
            index.Add(record);
            SaveIndex(gameRoot, index, policy);

            return new BackupResult { Ok = true, Record = record };
        }
        catch (Exception ex)
        {
            return BackupResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 判断定时备份是否到期（纯函数，便于自检）。
    /// 从未执行过时视为到期；<see cref="BackupSchedule.Off"/> 永不到期。
    /// </summary>
    public static bool IsScheduleDue(BackupPolicy policy, DateTime? now = null)
    {
        if (policy.Schedule == BackupSchedule.Off) return false;

        var t = now ?? DateTime.Now;
        var last = policy.LastScheduledRun;
        if (last is null) return true;

        return policy.Schedule switch
        {
            BackupSchedule.Daily => (t.Date - last.Value.Date).TotalDays >= 1,
            BackupSchedule.Weekly => (t.Date - last.Value.Date).TotalDays >= 7,
            BackupSchedule.Monthly => t.Year > last.Value.Year ||
                                      (t.Year == last.Value.Year && t.Month > last.Value.Month),
            _ => false
        };
    }

    /// <summary>定时频率的显示名。</summary>
    public static string ScheduleText(BackupSchedule s) => s switch
    {
        BackupSchedule.Daily => "每天",
        BackupSchedule.Weekly => "每周",
        BackupSchedule.Monthly => "每月",
        _ => "关闭"
    };

    /// <summary>
    /// 若定时备份到期则对指定目录各做一次自动备份，返回成功份数。
    /// 调用方负责在成功后持久化 <see cref="BackupPolicy.LastScheduledRun"/>。
    /// </summary>
    public static int RunScheduledIfDue(
        string gameRoot, IEnumerable<string> sourceDirs, BackupPolicy policy,
        BackupKind kind = BackupKind.Save, DateTime? now = null)
    {
        if (!IsScheduleDue(policy, now)) return 0;

        var n = 0;
        foreach (var dir in sourceDirs)
        {
            var r = Create(gameRoot, dir, kind,
                note: $"定时备份（{ScheduleText(policy.Schedule)}）", auto: true, policy: policy);
            if (r.Ok) n++;
        }

        if (n > 0)
        {
            policy.LastScheduledRun = now ?? DateTime.Now;
            Prune(gameRoot, policy, now);
        }
        return n;
    }

    /// <summary>
    /// 恢复备份，并按策略在恢复前先自动备份当前状态（规格 2.3-10）。
    /// 返回 (恢复结果, 恢复前自动备份的记录或 null)。
    /// </summary>
    public static (BackupResult Restore, BackupRecord? SafetyBackup) RestoreSafely(
        string gameRoot, BackupRecord record, string targetDir,
        BackupKind kind = BackupKind.Save, BackupPolicy? policy = null)
    {
        policy ??= new BackupPolicy();

        BackupRecord? safety = null;
        if (policy.BackupBeforeRestore && Directory.Exists(targetDir) &&
            Directory.EnumerateFileSystemEntries(targetDir).Any())
        {
            var pre = Create(gameRoot, targetDir, kind,
                note: $"恢复 {record.SourceName} 前的自动备份", auto: true, policy: policy);
            if (!pre.Ok)
                return (BackupResult.Fail($"恢复前备份失败，已中止：{pre.Error}"), null);
            safety = pre.Record;
        }

        return (Restore(record, targetDir, overwrite: true), safety);
    }

    /// <summary>
    /// 恢复备份到目标目录。<paramref name="overwrite"/> 为 true 时先清空目标目录，
    /// 否则目标目录非空即失败。
    /// </summary>
    public static BackupResult Restore(BackupRecord record, string targetDir, bool overwrite = false)
    {
        if (!record.Exists) return BackupResult.Fail("备份文件已丢失");

        try
        {
            if (Directory.Exists(targetDir))
            {
                if (!overwrite && Directory.EnumerateFileSystemEntries(targetDir).Any())
                    return BackupResult.Fail("目标目录非空，需勾选覆盖");
                if (overwrite) Directory.Delete(targetDir, recursive: true);
            }
            Directory.CreateDirectory(targetDir);
            ZipFile.ExtractToDirectory(record.ArchivePath, targetDir, overwriteFiles: true);
            return new BackupResult { Ok = true, Record = record };
        }
        catch (Exception ex)
        {
            return BackupResult.Fail(ex.Message);
        }
    }

    /// <summary>删除一条备份（含 zip 文件）。</summary>
    public static bool Delete(string gameRoot, string backupId, BackupPolicy? policy = null)
    {
        var index = LoadIndex(gameRoot, policy);
        var rec = index.FirstOrDefault(r => r.Id == backupId);
        if (rec is null) return false;

        try { if (File.Exists(rec.ArchivePath)) File.Delete(rec.ArchivePath); } catch { /* ignore */ }
        index.Remove(rec);
        SaveIndex(gameRoot, index, policy);
        return true;
    }

    /// <summary>
    /// 计算按策略应被淘汰的备份（纯函数，便于自检）：
    /// 先按超龄剔除自动备份，再按"每来源保留 N 份"保留最新的。手动备份不受超龄限制。
    /// </summary>
    public static List<BackupRecord> SelectExpired(
        IEnumerable<BackupRecord> records, BackupPolicy policy, DateTime? now = null)
    {
        var t = now ?? DateTime.Now;
        var all = records.ToList();
        var expired = new List<BackupRecord>();

        if (policy.MaxAgeDays > 0)
        {
            expired.AddRange(all.Where(r => r.Auto && (t - r.CreatedAt).TotalDays > policy.MaxAgeDays));
        }

        if (policy.KeepPerSource > 0)
        {
            foreach (var group in all.Except(expired).GroupBy(r => (r.Kind, r.SourceName)))
            {
                var ordered = group.OrderByDescending(r => r.CreatedAt).ToList();
                if (ordered.Count > policy.KeepPerSource)
                    expired.AddRange(ordered.Skip(policy.KeepPerSource));
            }
        }

        return expired.Distinct().ToList();
    }

    /// <summary>按策略清理，返回实际删除的份数。</summary>
    public static int Prune(string gameRoot, BackupPolicy policy, DateTime? now = null)
    {
        var index = LoadIndex(gameRoot, policy);
        var expired = SelectExpired(index, policy, now);
        if (expired.Count == 0) return 0;

        foreach (var rec in expired)
        {
            try { if (File.Exists(rec.ArchivePath)) File.Delete(rec.ArchivePath); } catch { /* ignore */ }
            index.Remove(rec);
        }
        SaveIndex(gameRoot, index, policy);
        return expired.Count;
    }

    /// <summary>列出备份（按时间倒序，可按类型过滤）。</summary>
    public static List<BackupRecord> List(string gameRoot, BackupKind? kind = null, BackupPolicy? policy = null) =>
        LoadIndex(gameRoot, policy)
            .Where(r => kind is null || r.Kind == kind)
            .OrderByDescending(r => r.CreatedAt)
            .ToList();

    /// <summary>统计占用空间（字节）。</summary>
    public static long TotalSize(string gameRoot, BackupPolicy? policy = null) =>
        LoadIndex(gameRoot, policy).Sum(r => r.SizeBytes);
}
