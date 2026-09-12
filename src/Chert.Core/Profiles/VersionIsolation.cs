using Chert.Core.Utils;

namespace Chert.Core.Profiles;

/// <summary>
/// 版本隔离：决定某个版本的「游戏工作目录」是共享的 <c>.minecraft</c> 还是
/// 独立的 <c>.minecraft/versions/&lt;id&gt;</c>。
/// <para>
/// 隔离后 mods / config / resourcepacks / saves 各版本互不干扰，
/// 这是整合包能并存的前提（规格 3.13「多实例追踪」）。
/// 判定依据是版本目录下的标记文件 <c>.mclcs-isolated</c>，纯文件系统状态、不依赖额外配置库。
/// </para>
/// </summary>
public static class VersionIsolation
{
    /// <summary>隔离标记文件名。</summary>
    public const string MarkerFileName = ".mclcs-isolated";

    /// <summary>隔离时会各自独立的子目录（用于展示与清理）。</summary>
    public static readonly string[] IsolatedFolders =
    {
        "mods", "config", "resourcepacks", "shaderpacks", "saves", "screenshots", "logs", "crash-reports"
    };

    /// <summary>标记文件路径。</summary>
    public static string MarkerPath(string gameRoot, string versionId) =>
        Path.Combine(PathEx.VersionDir(gameRoot, versionId), MarkerFileName);

    /// <summary>该版本是否启用了隔离。</summary>
    public static bool IsIsolated(string gameRoot, string versionId) =>
        !string.IsNullOrWhiteSpace(versionId) && File.Exists(MarkerPath(gameRoot, versionId));

    /// <summary>
    /// 该版本运行时的游戏工作目录：隔离 → <c>versions/&lt;id&gt;</c>，否则 → <c>gameRoot</c>。
    /// 这个值会作为 <c>--gameDir</c> / <c>${game_directory}</c> 传给游戏。
    /// </summary>
    public static string GameDirFor(string gameRoot, string versionId) =>
        IsIsolated(gameRoot, versionId) ? PathEx.VersionDir(gameRoot, versionId) : gameRoot;

    /// <summary>
    /// 开启隔离：创建版本目录与标记文件。<paramref name="note"/> 会写进标记文件，便于用户看出来源。
    /// 已开启时为幂等操作。
    /// </summary>
    public static string Enable(string gameRoot, string versionId, string? note = null)
    {
        var dir = PathEx.VersionDir(gameRoot, versionId);
        Directory.CreateDirectory(dir);

        var marker = Path.Combine(dir, MarkerFileName);
        if (!File.Exists(marker))
        {
            var content = string.IsNullOrWhiteSpace(note)
                ? $"MCLCS 版本隔离\n创建于 {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n"
                : $"MCLCS 版本隔离\n创建于 {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n来源：{note}\n";
            File.WriteAllText(marker, content);
        }
        return dir;
    }

    /// <summary>
    /// 关闭隔离：仅删除标记文件，<b>不会</b>删除已有的 mods / saves 等数据，
    /// 避免用户误操作丢档（数据会变成"孤儿目录"留在版本目录里，可手动搬运）。
    /// </summary>
    public static void Disable(string gameRoot, string versionId)
    {
        var marker = MarkerPath(gameRoot, versionId);
        if (File.Exists(marker)) File.Delete(marker);
    }

    /// <summary>为隔离版本预建常用子目录，避免游戏首次启动时缺目录。</summary>
    public static void EnsureFolders(string gameDir)
    {
        foreach (var f in IsolatedFolders)
            Directory.CreateDirectory(Path.Combine(gameDir, f));
    }

    /// <summary>
    /// 把整合包名清洗成合法的版本 Id（会成为 versions 下的目录名）。
    /// </summary>
    public static string SafeVersionId(string? name, string? fallback = null)
    {
        var s = (name ?? "").Trim();
        if (s.Length == 0) s = (fallback ?? "").Trim();
        if (s.Length == 0) s = "整合包";

        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        // 版本 Id 会出现在启动参数里，避开容易惹麻烦的字符
        foreach (var c in new[] { ' ', '\t' }) s = s.Replace(c, '-');
        s = s.Trim('.', '-', '_');
        if (s.Length == 0) s = "整合包";
        return s.Length > 64 ? s[..64] : s;
    }

    /// <summary>版本 Id 重名时追加 " (2)" 风格后缀。</summary>
    public static string ResolveIdConflict(string gameRoot, string versionId, out bool renamed)
    {
        renamed = false;
        var versionsDir = PathEx.VersionsDir(gameRoot);
        if (!Directory.Exists(Path.Combine(versionsDir, versionId))) return versionId;

        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{versionId}-{i}";
            if (Directory.Exists(Path.Combine(versionsDir, candidate))) continue;
            renamed = true;
            return candidate;
        }
        renamed = true;
        return $"{versionId}-{DateTime.Now:yyyyMMddHHmmss}";
    }
}
