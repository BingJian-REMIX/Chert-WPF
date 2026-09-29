using System.IO;

namespace Chert.App.ViewModels;

/// <summary>
/// 清单 #64：某个版本工作目录下的内容安装情况快照，
/// 支撑「安装了这个 Mod / 材质包 / 光影 / 数据包 / 存档 的版本」这类多维度分类检索。
/// </summary>
public sealed class VersionContentSnapshot
{
    public int Mods { get; init; }
    public int ResourcePacks { get; init; }
    public int ShaderPacks { get; init; }
    public int DataPacks { get; init; }
    public int Saves { get; init; }

    public static VersionContentSnapshot Empty { get; } = new();

    public bool HasAny => Mods + ResourcePacks + ShaderPacks + DataPacks + Saves > 0;

    /// <summary>按内容类别取值；类别键与 UI ComboBox 的 Tag 一致。</summary>
    public int CountOf(string? key) => key switch
    {
        "mods" => Mods,
        "resourcepacks" => ResourcePacks,
        "shaderpacks" => ShaderPacks,
        "datapacks" => DataPacks,
        "saves" => Saves,
        _ => 0
    };

    /// <summary>列表项上的摘要文案（如「Mod 12 · 光影 3」），无内容时为空。</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Mods > 0) parts.Add($"Mod {Mods}");
            if (ResourcePacks > 0) parts.Add($"材质 {ResourcePacks}");
            if (ShaderPacks > 0) parts.Add($"光影 {ShaderPacks}");
            if (DataPacks > 0) parts.Add($"数据包 {DataPacks}");
            if (Saves > 0) parts.Add($"存档 {Saves}");
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// 扫描版本工作目录（受版本隔离影响的实际目录）下各类可选内容的数量。
/// 只做一层文件系统枚举，失败一律按 0 处理，不抛异常。
/// </summary>
public static class VersionContentScanner
{
    private static readonly string[] ModExts = { ".jar", ".zip", ".disabled" };
    private static readonly string[] PackExts = { ".zip", ".disabled" };

    public static VersionContentSnapshot Scan(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return VersionContentSnapshot.Empty;

        return new VersionContentSnapshot
        {
            Mods = CountFiles(Path.Combine(dir, "mods"), ModExts),
            ResourcePacks = CountFiles(Path.Combine(dir, "resourcepacks"), PackExts),
            ShaderPacks = CountFiles(Path.Combine(dir, "shaderpacks"), PackExts),
            DataPacks = CountFiles(Path.Combine(dir, "datapacks"), PackExts),
            Saves = CountDirectories(Path.Combine(dir, "saves"))
        };
    }

    private static int CountFiles(string path, string[] exts)
    {
        try
        {
            if (!Directory.Exists(path)) return 0;
            return Directory.EnumerateFiles(path)
                .Count(f => exts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
        }
        catch { return 0; }
    }

    private static int CountDirectories(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.EnumerateDirectories(path).Count() : 0;
        }
        catch { return 0; }
    }
}
