using Chert.Core.Download;
using Chert.Core.Installers;
using Chert.Core.Models;
using Chert.Core.Profiles;
using Xunit;

namespace Chert.Core.Tests;

/// <summary>整合包在线源（Modrinth）与版本隔离的纯函数自检。</summary>
public class ModpackSourceTests
{
    // ---- ModrinthModpackSource ----

    [Fact]
    public void MapVersions_PrefersMrpack_ThenPrimary()
    {
        var versions = new List<ModrinthVersion>
        {
            new()
            {
                Id = "v1", Name = "1.0", VersionNumber = "1.0",
                GameVersions = new() { "1.20.1" }, Loaders = new() { "fabric" },
                Files = new()
                {
                    new ModrinthFile { FileName = "pack.jar", Url = "u1", Primary = true },
                    new ModrinthFile { FileName = "pack.mrpack", Url = "u2" }
                }
            }
        };

        var mapped = ModrinthModpackSource.MapVersions(versions);
        Assert.Single(mapped);
        Assert.Equal("pack.mrpack", mapped[0].FileName);
        Assert.Equal("u2", mapped[0].FileUrl);
        Assert.Equal("1.20.1", mapped[0].GameVersion);
        Assert.Equal("fabric", mapped[0].Loader);
        Assert.True(mapped[0].FileSize == 0);
    }

    [Fact]
    public void MapVersions_SkipsVersionsWithoutFile()
    {
        var versions = new List<ModrinthVersion>
        {
            new() { Id = "empty", Files = new() }
        };
        Assert.Empty(ModrinthModpackSource.MapVersions(versions));
    }

    [Fact]
    public void SortVersionsDescending_PutsNewestFirst()
    {
        var sorted = ModrinthModpackSource.SortVersionsDescending(new[] { "1.12.2", "1.20.1", "1.7.10" });
        Assert.Equal(new[] { "1.20.1", "1.12.2", "1.7.10" }, sorted);
    }

    [Fact]
    public void SortVersionsDescending_Dedups()
    {
        var sorted = ModrinthModpackSource.SortVersionsDescending(new[] { "1.20.1", "1.20.1", "1.19" });
        Assert.Equal(2, sorted.Count);
        Assert.Equal("1.20.1", sorted[0]);
    }
    // ---- VersionIsolation（独立版本隔离目录）----

    [Fact]
    public void SafeVersionId_SanitizesInvalidChars()
    {
        // 空格统一转 '-'（显式替换逻辑，与平台无关）
        Assert.Equal("a-b", VersionIsolation.SafeVersionId("a b"));
        // 空名回退到默认
        Assert.Equal("整合包", VersionIsolation.SafeVersionId(""));
        // 超长名截断到 64
        var longName = new string('A', 100);
        Assert.Equal(64, VersionIsolation.SafeVersionId(longName).Length);
    }

    [Fact]
    public void GameDirFor_RespectsIsolationMarker()
    {
        var root = Path.Combine(Path.GetTempPath(), "mclcs_iso_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(root, VersionIsolation.GameDirFor(root, "vanilla")); // 无标记 → 共享目录

            VersionIsolation.Enable(root, "rlcraft", "RLCraft");
            Assert.True(VersionIsolation.IsIsolated(root, "rlcraft"));
            var expected = Path.Combine(root, "versions", "rlcraft");
            Assert.Equal(expected, VersionIsolation.GameDirFor(root, "rlcraft"));
            // Enable 仅建目录 + 标记；子目录由 EnsureFolders 在启动前预建
            VersionIsolation.EnsureFolders(expected);
            Assert.True(Directory.Exists(Path.Combine(expected, "mods")));

            VersionIsolation.Disable(root, "rlcraft");
            Assert.False(VersionIsolation.IsIsolated(root, "rlcraft"));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* 忽略 */ }
        }
    }

    [Fact]
    public void ResolveIdConflict_AppendsSuffixOnClash()
    {
        var root = Path.Combine(Path.GetTempPath(), "mclcs_conflict_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "versions", "rlcraft"));

            var id = VersionIsolation.ResolveIdConflict(root, "rlcraft", out var renamed);
            Assert.True(renamed);
            Assert.Equal("rlcraft-2", id);

            VersionIsolation.ResolveIdConflict(root, "fresh", out var freshRenamed);
            Assert.False(freshRenamed);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* 忽略 */ }
        }
    }

    // ---- ModpackInstallResult 辅助 ----
    [Fact]
    public void ModpackInstallResult_CarriesIsolatedFlag()
    {
        var r = new ModpackInstallResult { Name = "RLCraft", VersionId = "rlcraft", Isolated = true, ModCount = 137 };
        Assert.True(r.Isolated);
        Assert.Equal(137, r.ModCount);
    }
}

