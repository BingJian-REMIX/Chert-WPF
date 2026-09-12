using System.Globalization;

namespace Chert.Core.Save;

/// <summary>
/// 游戏版本号 ↔ DataVersion（世界版本）对照表。
/// <para>
/// 数据来源：Minecraft Wiki «Data version»，仅收录常用正式版锚点。
/// 未收录的版本（如快照、极老版本）将按"未知版本"处理——兼容性比较与降级仍可执行，
/// 但版本字符串显示为未知；后续可结合版本清单（version_manifest）在线扩充。
/// </para>
/// <para>
/// 版本命名支持两套方案：
/// <list type="bullet">
///   <item>旧方案 <c>1.X.Y</c>（如 1.20.1 / 1.21.11），首位固定为 "1"。</item>
///   <item>新方案 <c>YY.M[.P]</c>（如 26.1 / 26.1.2），即"年份后二位.月份[.补丁]"，
///   自 1.21.x 之后、跳过不存在的 1.22.0 起启用。</item>
/// </list>
/// 两套方案共用同一张对照表，<see cref="ToDataVersion"/> / <see cref="ToGameVersion"/> 均可无差别查表。
/// </para>
/// </summary>
public static class DataVersionMap
{
    // 版本号字符串 -> DataVersion（按 Wiki 精确值）
    private static readonly Dictionary<string, int> VersionToData = new(StringComparer.Ordinal)
    {
        ["1.12.2"] = 1343,
        ["1.13.2"] = 1631,
        ["1.14.4"] = 1976,
        ["1.15.2"] = 2230,
        ["1.16.5"] = 2586,
        ["1.17.1"] = 2730,
        ["1.18.2"] = 2975,
        ["1.19"]   = 3105,
        ["1.19.2"] = 3120,
        ["1.19.4"] = 3337,
        ["1.20"]   = 3463,
        ["1.20.1"] = 3465,
        ["1.20.2"] = 3578,
        ["1.20.4"] = 3700,
        ["1.20.5"] = 3837,
        ["1.20.6"] = 3839,
        ["1.21"]   = 3953,
        ["1.21.1"] = 3955,
        ["1.21.2"] = 4080,
        ["1.21.3"] = 4082,
        ["1.21.4"] = 4189,
        ["1.21.5"] = 4325,
        ["1.21.6"] = 4435,
        ["1.21.7"] = 4438,
        ["1.21.8"] = 4440,
        // 旧方案延续到 1.21.11 后，跳过不存在的 1.22.0，改为新方案 YY.M
        ["1.21.9"]  = 4554,
        ["1.21.10"] = 4556,
        ["1.21.11"] = 4671,
        // ---- 新命名方案 YY.M（年份后二位.月份[.补丁]）----
        ["26.1"]    = 4786,
        ["26.1.1"]  = 4788,
        ["26.1.2"]  = 4790,
        ["26.2"]    = 4903,
        // 26.3 尚未正式发布，取其快照已达到的 DataVersion 作为最新已知锚点
        ["26.3"]    = 5005
    };

    // DataVersion -> 版本号字符串（反向索引，便于从存档反查；重复 dv 取首个版本名）
    private static readonly Dictionary<int, string> DataToVersion = BuildReverse();

    private static Dictionary<int, string> BuildReverse()
    {
        var map = new Dictionary<int, string>();
        foreach (var kv in VersionToData)
            if (!map.ContainsKey(kv.Value)) map[kv.Value] = kv.Key;
        return map;
    }

    /// <summary>游戏版本号 -> DataVersion；未收录返回 null。</summary>
    public static int? ToDataVersion(string gameVersionId)
    {
        if (string.IsNullOrWhiteSpace(gameVersionId)) return null;
        // 优先精确匹配（如 "1.20.1"）
        if (VersionToData.TryGetValue(gameVersionId, out var dv)) return dv;

        // 兼容形如 "fabric-1.20.1" / "forge-1.19.4" 的版本 id：提取其中 x.y.z
        var m = System.Text.RegularExpressions.Regex.Match(gameVersionId, @"\d+\.\d+(?:\.\d+)?");
        if (m.Success && VersionToData.TryGetValue(m.Value, out var dv2)) return dv2;

        return null;
    }

    /// <summary>DataVersion -> 游戏版本号字符串；未收录返回 null。</summary>
    public static string? ToGameVersion(int dataVersion)
        => DataToVersion.TryGetValue(dataVersion, out var v) ? v : null;

    /// <summary>DataVersion -> 展示用版本字符串；未知时返回 "未知 (dv=NNNN)"。</summary>
    public static string DescribeDataVersion(int dataVersion)
        => ToGameVersion(dataVersion) ?? $"未知 (dv={dataVersion})";

    /// <summary>尝试将版本号字符串规范化为最短已知形式（去除加载器前缀）。</summary>
    public static string NormalizeVersion(string gameVersionId)
    {
        var m = System.Text.RegularExpressions.Regex.Match(gameVersionId, @"\d+\.\d+(?:\.\d+)?");
        return m.Success ? m.Value : gameVersionId;
    }

    /// <summary>比较两个游戏版本号的高低（基于 DataVersion）。返回 -1/0/1 或 null（任一未知）。</summary>
    public static int? CompareVersions(string a, string b)
    {
        var da = ToDataVersion(a);
        var db = ToDataVersion(b);
        if (da is null || db is null) return null;
        return da.Value.CompareTo(db.Value);
    }

    /// <summary>返回对照表中全部已知版本号（升序），用于 UI 下拉选择降级目标。</summary>
    public static IReadOnlyList<string> KnownVersions()
        => VersionToData.Keys
            .OrderBy(k => VersionToData[k])
            .ToList();
}
