using System.IO.Compression;

namespace Chert.Core.Toolbox;

/// <summary>一张截图的信息。</summary>
public class ScreenshotInfo
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime CreatedUtc { get; set; }
}

/// <summary>
/// 截图管理器（工具箱功能 4）：浏览、查看、打包分享、批量删除游戏截图（<c>screenshots/</c>）。
/// </summary>
public static class ScreenshotManager
{
    public static string ScreenshotsDir(string gameRoot) => Path.Combine(gameRoot, "screenshots");

    private static readonly string[] _exts = { ".png", ".jpg", ".jpeg" };

    /// <summary>列出全部截图（按拍摄时间倒序）。</summary>
    public static List<ScreenshotInfo> ListScreenshots(string gameRoot)
    {
        var dir = ScreenshotsDir(gameRoot);
        var list = new List<ScreenshotInfo>();
        if (!Directory.Exists(dir)) return list;
        foreach (var file in Directory.GetFiles(dir))
        {
            if (Array.Exists(_exts, e => file.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                list.Add(new ScreenshotInfo
                {
                    Name = Path.GetFileName(file),
                    FullPath = file,
                    SizeBytes = new FileInfo(file).Length,
                    CreatedUtc = File.GetCreationTimeUtc(file)
                });
        }
        list.Sort((a, b) => b.CreatedUtc.CompareTo(a.CreatedUtc));
        return list;
    }

    /// <summary>批量删除截图；返回成功删除的数量。</summary>
    public static int DeleteScreenshots(IEnumerable<string> paths)
    {
        var ok = 0;
        foreach (var p in paths)
        {
            try { if (File.Exists(p)) { File.Delete(p); ok++; } }
            catch { /* 忽略单个失败 */ }
        }
        return ok;
    }

    /// <summary>将选中的截图打包为 zip 分享；返回产物路径。</summary>
    public static string Package(IEnumerable<string> paths, string destZip)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destZip) ?? ".");
        if (File.Exists(destZip)) File.Delete(destZip);
        using var zip = ZipFile.Open(destZip, ZipArchiveMode.Create);
        foreach (var p in paths)
        {
            if (!File.Exists(p)) continue;
            var entryName = "screenshots/" + Path.GetFileName(p);
            zip.CreateEntryFromFile(p, entryName);
        }
        return destZip;
    }
}
