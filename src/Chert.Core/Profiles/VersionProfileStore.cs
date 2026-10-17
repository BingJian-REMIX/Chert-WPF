using System.IO;
using System.Text.Json;
using Chert.Core.Models;
using Chert.Core.Utils;

namespace Chert.Core.Profiles;

/// <summary>
/// 每版本配置读写（versions/&lt;id&gt;/profile.json）+ 有效游戏目录解析。
/// <para>
/// 隔离优先级：<b>自定义目录 &gt; 自动隔离(versions/&lt;id&gt;) &gt; 共享根</b>。
/// <para>自动隔离的<b>唯一真值</b>是 <see cref="VersionIsolation"/> 的 .mclcs-isolated 标记，
/// 与启动链路 <see cref="VersionIsolation.GameDirFor"/> 完全一致：profile.json 里的
/// Isolation=Auto 只在标记存在时成立，不再单独生效。这样「版本设置显示」与
/// 「游戏实际工作目录」永不分裂（issue #5 bug1/bug4）。</para>
/// </para>
/// </summary>
public static class VersionProfileStore
{
    private static string ProfilePath(string gameRoot, string id) =>
        Path.Combine(PathEx.VersionDir(gameRoot, id), "profile.json");

    /// <summary>加载每版本配置；目录或文件不存在时返回默认（Auto 隔离）配置。</summary>
    public static VersionProfile Load(string gameRoot, string id)
    {
        var path = ProfilePath(gameRoot, id);
        if (!File.Exists(path))
        {
            // issue #5：无 profile.json 时，默认隔离状态一律以 .mclcs-isolated 标记为准
            // （方案甲：标记存在 = Auto，不存在 = Shared），与启动链路 GameDirFor 完全一致。
            // 全局「新建版本默认隔离」改由安装完成时落地（LauncherService.ApplyDefaultIsolation），
            // 不再在读取时凭空套用 —— 否则会出现「读出来是 Auto、标记却没有」的分裂。
            var def = new VersionProfile();
            try
            {
                def.Isolation = VersionIsolation.IsIsolated(gameRoot, id)
                    ? IsolationMode.Auto
                    : IsolationMode.Shared;
            }
            catch { }
            return def;
        }
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<VersionProfile>(json) ?? new VersionProfile();
        }
        catch
        {
            return new VersionProfile();
        }
    }

    /// <summary>保存每版本配置（先确保 versions/&lt;id&gt; 目录存在）。</summary>
    public static void Save(string gameRoot, string id, VersionProfile profile)
    {
        var dir = PathEx.VersionDir(gameRoot, id);
        Directory.CreateDirectory(dir);
        profile.UpdatedAt = DateTime.Now;
        var path = Path.Combine(dir, "profile.json");
        var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// 该版本是否<b>已显式保存过</b>每版本配置（<c>versions/&lt;id&gt;/profile.json</c> 存在）。
    /// WPF 专用：仅在用户手动保存过版本设置后才用覆盖层决定工作目录，
    /// 否则沿用 <see cref="VersionIsolation"/> 的 .mclcs-isolated 标记行为，
    /// 避免把既有共享目录的老版本静默搬到 versions/&lt;id&gt;。
    /// </summary>
    public static bool HasProfile(string gameRoot, string id) => File.Exists(ProfilePath(gameRoot, id));

    /// <summary>该版本是否处于锁定状态（锁定后阻止改写其文件，如安装加载器 / 增删 Mod）。</summary>
    public static bool IsLocked(string gameRoot, string id)
    {
        try { return Load(gameRoot, id).Locked; }
        catch { return false; }
    }

    /// <summary>
    /// 计算该版本运行时的有效游戏目录（纯函数，无副作用）。
    /// <list type="bullet">
    ///   <item>Custom 且路径非空 → 自定义目录（物理隔离）；</item>
    ///   <item>Auto → versions/&lt;id&gt;；</item>
    ///   <item>Shared 或兜底 → 共享根目录。</item>
    /// </list>
    /// </summary>
    public static string EffectiveGameDir(string gameRoot, string id, VersionProfile profile)
    {
        // 方案甲：.mclcs-isolated 标记才是隔离模式的唯一真值：目录已隔离则一律走 versions/<id>，
        // 无论 profile.json 里存的是 Auto 还是 Shared（文件夹状态优先于存储字段）。
        if (VersionIsolation.IsIsolated(gameRoot, id))
            return PathEx.VersionDir(gameRoot, id);
        if (profile.Isolation == IsolationMode.Custom &&
            !string.IsNullOrWhiteSpace(profile.CustomGameDir))
            return profile.CustomGameDir!;
        // 无标记时不再按 profile 的 Auto 偷偷隔离：与启动链路 GameDirFor 保持一致。
        // 此前这里会返回 versions/<id>，于是 mods / resourcepacks 被装进版本目录，
        // 而游戏启动时按标记判定去 gameRoot 找 → 加载不到 mod、语言文件丢失（issue #5 bug4）。
        return gameRoot;
    }

    /// <summary>
    /// 应用隔离模式到文件系统（在保存后调用）：Auto 创建标记+预建子目录，
    /// Custom 确保目录存在，Shared 移除自动隔离标记。
    /// </summary>
    public static void ApplyIsolation(string gameRoot, string id, VersionProfile profile)
    {
        var effective = EffectiveGameDir(gameRoot, id, profile);
        switch (profile.Isolation)
        {
            case IsolationMode.Auto:
                VersionIsolation.Enable(gameRoot, id, "版本设置-自动隔离");
                VersionIsolation.EnsureFolders(effective);
                break;
            case IsolationMode.Custom:
                Directory.CreateDirectory(effective);
                VersionIsolation.EnsureFolders(effective);
                break;
            case IsolationMode.Shared:
            default:
                VersionIsolation.Disable(gameRoot, id);
                break;
        }
    }

    /// <summary>检测该版本当前已安装的模组加载器（依据 .fabric 标记 / version.json inheritsFrom）。</summary>
    public static ModLoaderKind DetectLoader(string gameRoot, string id)
    {
        if (Directory.Exists(PathEx.FabricMarker(gameRoot, id)))
            return ModLoaderKind.Fabric;

        try
        {
            var json = PathEx.VersionJsonPath(gameRoot, id);
            if (File.Exists(json))
            {
                var v = JsonSerializer.Deserialize<VersionJson>(File.ReadAllText(json));
                if (v is not null && !string.IsNullOrEmpty(v.InheritsFrom))
                {
                    var lower = id.ToLowerInvariant();
                    if (lower.Contains("neoforge")) return ModLoaderKind.NeoForge;
                    if (lower.Contains("forge")) return ModLoaderKind.Forge;
                    if (lower.Contains("quilt")) return ModLoaderKind.Quilt;
                    if (lower.Contains("fabric")) return ModLoaderKind.Fabric;
                    // 有 inheritsFrom 但 id 不带关键字：按 MainClass 推断
                    if (v.MainClass.Contains("fabricmc", StringComparison.OrdinalIgnoreCase)) return ModLoaderKind.Fabric;
                    if (v.MainClass.Contains("forge", StringComparison.OrdinalIgnoreCase)) return ModLoaderKind.Forge;
                    return ModLoaderKind.Forge; // 有 inheritsFrom 通常即加载器变体
                }
            }
        }
        catch { /* 忽略解析错误 */ }
        return ModLoaderKind.None;
    }

    /// <summary>
    /// 解析该版本对应的原版基版本号（用于安装加载器）。
    /// 优先读 version.json 的 inheritsFrom；否则取版本 Id 本身（原版）。
    /// </summary>
    public static string BaseMcVersion(string gameRoot, string id)
    {
        try
        {
            var json = PathEx.VersionJsonPath(gameRoot, id);
            if (File.Exists(json))
            {
                var v = JsonSerializer.Deserialize<VersionJson>(File.ReadAllText(json));
                if (v is not null && !string.IsNullOrEmpty(v.InheritsFrom))
                    return v.InheritsFrom!;
            }
        }
        catch { /* 忽略 */ }
        return id;
    }
}
