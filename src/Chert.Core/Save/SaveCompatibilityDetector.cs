using System.Globalization;
using System.Text.RegularExpressions;

namespace Chert.Core.Save;

/// <summary>
/// §二.4 存档兼容性检测。
/// <para>
/// 在启动某版本前扫描 <c>saves/</c> 下每个世界的 <c>level.dat</c>，读取其中的 DataVersion，
/// 与待启动游戏版本的 DataVersion 比较。若存档版本高于游戏版本，则判定不兼容，
/// 并给出三选一处置（安装对应版本 / 降级存档 / 忽略）。
/// 全程只读，不产生任何副作用。
/// </para>
/// </summary>
public static class SaveCompatibilityDetector
{
    /// <summary>saves 目录路径。</summary>
    public static string SavesDir(string gameRoot) => Path.Combine(gameRoot, "saves");

    /// <summary>某存档的 level.dat 路径。</summary>
    public static string LevelDatPath(string savePath) => Path.Combine(savePath, "level.dat");

    /// <summary>
    /// 扫描全部存档的兼容性。跳过无 level.dat 或损坏的存档（不抛异常）。
    /// </summary>
    public static List<SaveCompatibilityReport> Scan(string gameRoot, string gameVersionId)
    {
        var result = new List<SaveCompatibilityReport>();
        var savesDir = SavesDir(gameRoot);
        if (!Directory.Exists(savesDir)) return result;

        foreach (var dir in Directory.GetDirectories(savesDir))
        {
            var saveName = Path.GetFileName(dir);
            // 跳过备份目录（形如 <name>.backup-<ts>）
            if (saveName.EndsWith(".backup", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(saveName, @"\.backup-\d{14}$")) continue;

            var report = CheckSingleSave(dir, gameVersionId);
            if (report is not null) result.Add(report);
        }
        return result;
    }

    /// <summary>
    /// 检测单个存档的兼容性；level.dat 缺失或解析失败返回 null。
    /// </summary>
    public static SaveCompatibilityReport? CheckSingleSave(string savePath, string gameVersionId)
    {
        var levelDat = LevelDatPath(savePath);
        if (!File.Exists(levelDat)) return null;

        int saveDataVersion;
        try
        {
            var root = NbtFile.ReadGzip(levelDat);
            saveDataVersion = root.GetDataVersion();
        }
        catch (Exception ex)
        {
            return new SaveCompatibilityReport
            {
                SaveName = Path.GetFileName(savePath),
                SavePath = savePath,
                SaveDataVersion = 0,
                GameVersionId = gameVersionId,
                Compatible = false,
                Severity = SaveCompatibilitySeverity.Unknown,
                Message = $"无法解析 level.dat：{ex.Message}"
            };
        }

        var gameDv = DataVersionMap.ToDataVersion(gameVersionId);
        var saveVersion = DataVersionMap.ToGameVersion(saveDataVersion);
        var saveName = Path.GetFileName(savePath);

        var report = new SaveCompatibilityReport
        {
            SaveName = saveName,
            SavePath = savePath,
            SaveDataVersion = saveDataVersion,
            SaveGameVersion = saveVersion,
            GameVersionId = gameVersionId,
            GameDataVersion = gameDv,
            HasBackup = FindBackups(Path.GetDirectoryName(savePath) ?? "", saveName).Count > 0
        };

        if (gameDv is null)
        {
            report.Compatible = true; // 无法比较，保守视为兼容（由游戏自行警告）
            report.Severity = SaveCompatibilitySeverity.Unknown;
            report.Message = $"游戏版本 {gameVersionId} 不在对照表中，无法比较；该存档由 "
                             + $"{DataVersionMap.DescribeDataVersion(saveDataVersion)} 创建。";
            return report;
        }

        if (saveDataVersion <= gameDv.Value)
        {
            report.Compatible = true;
            report.Severity = SaveCompatibilitySeverity.Ok;
            report.Message = $"存档（{DataVersionMap.DescribeDataVersion(saveDataVersion)}）"
                             + $"兼容游戏版本 {gameVersionId}。";
        }
        else
        {
            report.Compatible = false;
            report.Severity = Classify(saveDataVersion, gameDv.Value);
            report.RecommendedAction = SaveCompatAction.Downgrade;
            report.Message = $"存档由 {DataVersionMap.DescribeDataVersion(saveDataVersion)} 创建，"
                             + $"高于当前游戏版本 {gameVersionId}（dv={gameDv}）。"
                             + (report.Severity == SaveCompatibilitySeverity.MuchNewer
                                 ? "跨多个大版本，降级有数据丢失风险。"
                                 : "")
                             + "建议降级存档或安装对应版本。";
        }

        return report;
    }

    private static SaveCompatibilitySeverity Classify(int saveDv, int gameDv)
    {
        var saveMaj = MajorOf(DataVersionMap.ToGameVersion(saveDv));
        var gameMaj = MajorOf(DataVersionMap.ToGameVersion(gameDv));
        if (saveMaj.HasValue && gameMaj.HasValue)
            return (saveMaj.Value - gameMaj.Value) >= 2
                ? SaveCompatibilitySeverity.MuchNewer
                : SaveCompatibilitySeverity.SlightlyNewer;

        // 未知版本时退化为按 DataVersion 间距粗略判断
        return (saveDv - gameDv) >= 800
            ? SaveCompatibilitySeverity.MuchNewer
            : SaveCompatibilitySeverity.SlightlyNewer;
    }

    /// <summary>
    /// 判定版本命名方案。
    /// <list type="bullet">
    ///   <item>旧方案 <c>1.X.Y</c>：首位固定为 "1"（如 1.20.1 / 1.21.11）。</item>
    ///   <item>新方案 <c>YY.M[.P]</c>：首位为两位年份（≥20），次位为月份（如 26.1 / 26.1.2）。</item>
    /// </list>
    /// 其余格式保守按旧方案处理。
    /// </summary>
    private enum VersionScheme { Old, New }

    private static VersionScheme DetectScheme(string? version)
    {
        if (version is null) return VersionScheme.Old;
        var norm = DataVersionMap.NormalizeVersion(version);
        var parts = norm.Split('.');
        // 旧方案：首位固定为 "1"
        if (parts.Length >= 1 && parts[0] == "1") return VersionScheme.Old;
        // 新方案：首位为两位年份（>=20），次位为月份
        if (parts.Length >= 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var yy)
            && yy >= 20
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return VersionScheme.New;
        return VersionScheme.Old;
    }

    /// <summary>
    /// 提取用于严重程度判定的"大版本序号"。
    /// <list type="bullet">
    ///   <item>旧方案 <c>1.X.Y</c>：取 X（次位小版本），例如 1.20.1 → 20、1.21.11 → 21。</item>
    ///   <item>新方案 <c>YY.M[.P]</c>：取"自 2000 年起的累计月份" = YY×12 + M，
    ///   例如 26.1 → 26×12+1 = 313，26.2 → 314，27.1 → 27×12+1 = 325，
    ///   使同年跨月、跨年比较都保持单调且可比。</item>
    /// </list>
    /// 任一分量无法解析时回退为该版本首个可解析数字。
    /// </summary>
    private static int? MajorOf(string? version)
    {
        if (version is null) return null;
        var norm = DataVersionMap.NormalizeVersion(version);
        var parts = norm.Split('.');
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var a)) return null;
        if (parts.Length == 1) return a;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b)) return a;

        return DetectScheme(version) switch
        {
            VersionScheme.New => a * 12 + b,
            _ => b
        };
    }

    // ---- 备份发现（供 §三 / §四.2 复用） ----

    /// <summary>
    /// 查找某存档的全部降级备份（<c>saves/&lt;SaveName&gt;.backup-&lt;timestamp&gt;</c>），
    /// 按创建时间升序返回。
    /// </summary>
    public static List<SaveBackupInfo> FindBackups(string savesDir, string saveName)
    {
        var list = new List<SaveBackupInfo>();
        if (!Directory.Exists(savesDir)) return list;

        foreach (var dir in Directory.GetDirectories(savesDir))
        {
            var name = Path.GetFileName(dir);
            if (!name.StartsWith(saveName + ".backup", StringComparison.OrdinalIgnoreCase)) continue;

            var m = Regex.Match(name, @"\.backup-(\d{14})$");
            DateTime created = m.Success
                ? DateTime.ParseExact(m.Groups[1].Value, "yyyyMMddHHmmss",
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal)
                : Directory.GetCreationTimeUtc(dir);

            int? dv = null;
            var lvl = LevelDatPath(dir);
            if (File.Exists(lvl))
            {
                try { dv = NbtFile.ReadGzip(lvl).GetDataVersion(); } catch { /* 忽略 */ }
            }

            list.Add(new SaveBackupInfo
            {
                SaveName = saveName,
                BackupPath = dir,
                CreatedUtc = created,
                DataVersion = dv,
                GameVersion = dv.HasValue ? DataVersionMap.ToGameVersion(dv.Value) : null
            });
        }

        list.Sort((a, b) => a.CreatedUtc.CompareTo(b.CreatedUtc));
        return list;
    }
}
