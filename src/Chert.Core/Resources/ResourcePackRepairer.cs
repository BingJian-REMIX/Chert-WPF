using System.Text.Json;
using Chert.Core.Profiles;

namespace Chert.Core.Resources;

/// <summary>
/// 资源包格式自动修复（规格 3.13）：检测 pack_format 与 MC 版本不匹配时，
/// 调整 pack.mcmeta 中的 pack_format 字段。仅处理版本号问题。
/// </summary>
public static class ResourcePackRepairer
{
    /// <summary>MC 版本 → 推荐 pack_format 的映射。</summary>
    private static readonly Dictionary<int, int> VersionFormatMap = new()
    {
        { 12, 4 }, { 13, 4 }, { 14, 4 }, { 15, 5 }, { 16, 6 },
        { 17, 7 }, { 18, 8 }, { 19, 9 }, { 20, 15 }, { 21, 34 }
    };

    /// <summary>检查指定资源包的 pack_format 是否匹配目标 MC 主版本。</summary>
    public static (bool Match, int CurrentFormat, int ExpectedFormat) Check(string packDir, int mcMajorVersion)
    {
        var mcmeta = Path.Combine(packDir, "pack.mcmeta");
        if (!File.Exists(mcmeta)) return (false, 0, 0);

        try
        {
            var json = JsonDocument.Parse(File.ReadAllText(mcmeta));
            var currentFormat = json.RootElement
                .GetProperty("pack").GetProperty("pack_format").GetInt32();

            var expected = VersionFormatMap.GetValueOrDefault(mcMajorVersion, 34);
            return (currentFormat == expected, currentFormat, expected);
        }
        catch { return (false, 0, 0); }
    }

    /// <summary>修复 pack_format 为匹配值，修复前自动备份为 .backup。</summary>
    public static bool Repair(string packDir, int expectedFormat)
    {
        var mcmeta = Path.Combine(packDir, "pack.mcmeta");
        if (!File.Exists(mcmeta)) return false;

        try
        {
            // 备份
            var backup = mcmeta + ".backup";
            if (!File.Exists(backup)) File.Copy(mcmeta, backup, overwrite: true);

            var doc = JsonDocument.Parse(File.ReadAllText(mcmeta));
            var root = doc.RootElement;
            using var stream = new MemoryStream();
            var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            writer.WriteStartObject("pack");
            writer.WriteNumber("pack_format", expectedFormat);
            if (root.TryGetProperty("pack", out var pack) && pack.TryGetProperty("description", out var desc))
                writer.WriteString("description", desc.GetString() ?? "");
            writer.WriteEndObject();
            if (root.TryGetProperty("overlays", out _))
            {
                var overlays = root.GetProperty("overlays");
                overlays.WriteTo(writer);
            }
            writer.WriteEndObject();
            writer.Flush();
            File.WriteAllBytes(mcmeta, stream.ToArray());
            return true;
        }
        catch { return false; }
    }

    /// <summary>资源包 / 光影回滚到默认的结果。</summary>
    public class ResourcePackResetResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public List<string> Actions { get; } = new();
    }

    /// <summary>
    /// 资源包 / 光影崩溃的"安全回滚"：将 options.txt 的资源包重置为 vanilla，
    /// 停用 shaderpacks 目录，清空 cache 目录。全部为非破坏性操作（备份原文件 / 重命名目录，可随时恢复）。
    /// 用于崩溃自动修复（<see cref="RepairStrategy.ResetResourcePacks"/>）。
    /// </summary>
    public static ResourcePackResetResult ResetToVanilla(string gameRoot)
    {
        var result = new ResourcePackResetResult();
        try
        {
            // 1) options.txt：备份并重置资源包为 vanilla
            var options = Path.Combine(gameRoot, "options.txt");
            if (File.Exists(options))
            {
                var bak = options + ".mclcs-bak";
                if (!File.Exists(bak)) File.Copy(options, bak, overwrite: true);

                var lines = File.ReadAllLines(options).ToList();
                var idx = lines.FindIndex(l => l.StartsWith("resourcePacks:", StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                    lines[idx] = "resourcePacks:[\"vanilla\"]";
                else
                    lines.Add("resourcePacks:[\"vanilla\"]");
                File.WriteAllLines(options, lines);
                result.Actions.Add("options.txt 资源包已重置为 vanilla（原文件备份为 options.txt.mclcs-bak）。");
            }
            else
            {
                result.Actions.Add("未发现 options.txt，跳过资源包重置。");
            }

            // 2) 停用 shaderpacks 目录（如存在）
            var shaderDir = Path.Combine(gameRoot, "shaderpacks");
            var shaderDisabled = Path.Combine(gameRoot, "shaderpacks.disabled");
            if (Directory.Exists(shaderDir) && !Directory.Exists(shaderDisabled))
            {
                Directory.Move(shaderDir, shaderDisabled);
                result.Actions.Add("shaderpacks 目录已停用（重命名为 shaderpacks.disabled，可改回）。");
            }

            // 3) 停用 optionsshaders.txt 中的着色器（Iris / 部分加载器）
            var optShaders = Path.Combine(gameRoot, "optionsshaders.txt");
            if (File.Exists(optShaders))
            {
                var slines = File.ReadAllLines(optShaders).ToList();
                var sidx = slines.FindIndex(l => l.StartsWith("shaderPack=", StringComparison.OrdinalIgnoreCase));
                if (sidx >= 0) { slines[sidx] = "shaderPack=OFF"; }
                else { slines.Add("shaderPack=OFF"); }
                File.WriteAllLines(optShaders, slines);
                result.Actions.Add("optionsshaders.txt 中 shaderPack 已设为 OFF。");
            }

            // 4) 清空 cache 目录（重命名为 cache.disabled，游戏会重建）
            var cacheDir = Path.Combine(gameRoot, "cache");
            var cacheDisabled = Path.Combine(gameRoot, "cache.disabled");
            if (Directory.Exists(cacheDir) && !Directory.Exists(cacheDisabled))
            {
                Directory.Move(cacheDir, cacheDisabled);
                result.Actions.Add("cache 目录已清空（重命名为 cache.disabled，游戏会重建）。");
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
        }
        return result;
    }
}
