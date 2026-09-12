using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Chert.Core.Models;

namespace Chert.Core.Mods;

/// <summary>
/// 从 Mod JAR 中解析元数据（fabric.mod.json / mods.toml / neoforge.mods.toml）。
/// </summary>
public static class ModMetadataParser
{
    /// <summary>尝试从 jar 中提取并解析 fabric.mod.json。</summary>
    public static FabricModJson? ParseFabricMod(string jarPath)
    {
        var text = ReadEntryFromJar(jarPath, "fabric.mod.json");
        if (text is null) return null;
        try { return JsonSerializer.Deserialize<FabricModJson>(text); }
        catch { return null; }
    }

    /// <summary>尝试从 jar 中解析 Forge/NeoForge mods.toml。</summary>
    public static ForgeModMeta? ParseForgeMod(string jarPath)
    {
        // 优先 neoforge.mods.toml，回退 mods.toml
        var text = ReadEntryFromJar(jarPath, "META-INF/neoforge.mods.toml")
                   ?? ReadEntryFromJar(jarPath, "META-INF/mods.toml");
        if (text is null) return null;
        return ParseModsToml(text);
    }

    /// <summary>简易 TOML 解析（仅提取 [[mods]] 和 [[dependencies.*]] 关键字段）。</summary>
    private static ForgeModMeta? ParseModsToml(string toml)
    {
        var meta = new ForgeModMeta();
        var inMods = false;
        var inDeps = false;
        var currentDep = new ForgeModDependency();

        foreach (var rawLine in toml.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

            if (line.StartsWith("[[mods]]"))
            {
                inMods = true;
                inDeps = false;
                continue;
            }
            if (Regex.IsMatch(line, @"^\[\[dependencies\."))
            {
                inDeps = true;
                inMods = false;
                // 如果当前 dep 有 modId，先保存
                if (!string.IsNullOrEmpty(currentDep.ModId))
                {
                    meta.Dependencies.Add(currentDep);
                    currentDep = new ForgeModDependency();
                }
                continue;
            }
            if (line.StartsWith("[") && !line.StartsWith("[["))
            {
                inMods = false;
                inDeps = false;
                continue;
            }

            var eqIdx = line.IndexOf('=');
            if (eqIdx < 0) continue;
            var key = line[..eqIdx].Trim();
            var value = line[(eqIdx + 1)..].Trim().Trim('"');

            if (inMods)
            {
                switch (key)
                {
                    case "modId": meta.ModId = value; break;
                    case "displayName": meta.DisplayName = value; break;
                    case "version": meta.Version = value; break;
                }
            }
            else if (inDeps)
            {
                switch (key)
                {
                    case "modId": currentDep.ModId = value; break;
                    case "versionRange": currentDep.VersionRange = value; break;
                    case "mandatory": currentDep.Mandatory = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                    case "ordering": currentDep.Ordering = value.ToUpperInvariant(); break;
                    case "side": currentDep.Side = value.ToUpperInvariant(); break;
                }
            }
        }

        if (!string.IsNullOrEmpty(currentDep.ModId))
            meta.Dependencies.Add(currentDep);

        return string.IsNullOrEmpty(meta.ModId) ? null : meta;
    }

    /// <summary>从 jar 中读取指定路径的文本内容。</summary>
    private static string? ReadEntryFromJar(string jarPath, string entryName)
    {
        if (!File.Exists(jarPath)) return null;
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            var entry = archive.GetEntry(entryName);
            if (entry is null) return null;
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }
}
