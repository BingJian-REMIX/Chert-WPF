using System.Text.Json.Nodes;
using Chert.Core.Toolbox;
using Xunit;

namespace Chert.Core.Tests;

/// <summary>音乐播放器：MC 原声提取 + 播放列表导航逻辑自检（规格 2.3 面板 14）。</summary>
public class MusicTests
{
    // ---- McOstExtractor ----

    [Fact]
    public void Scan_GroupsMusicRecordsAmbient_AndSkipsNonMusic()
    {
        var root = PrepareRoot(out var versionId);

        var groups = McOstExtractor.Scan(root, versionId);
        var byCat = groups.ToDictionary(g => g.Category);

        Assert.True(byCat.ContainsKey("music"));
        Assert.True(byCat.ContainsKey("records"));
        Assert.True(byCat.ContainsKey("ambient"));

        // weather 不在 MusicPrefixes，应被忽略
        Assert.False(byCat.ContainsKey("other"));
        Assert.DoesNotContain(groups, g => g.Tracks.Any(t => t.FilePath.Contains("weather")));

        // 唱片分类 Artist 标注
        var rec = byCat["records"].Tracks[0];
        Assert.Equal("Minecraft 唱片", rec.ToTrack().Artist);
        Assert.StartsWith("唱片", rec.Title);

        Directory.Delete(root, true);
    }

    [Fact]
    public void Scan_MissingIndex_ReturnsEmpty()
    {
        var root = Path.Combine(Path.GetTempPath(), "mclcs_ost_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var groups = McOstExtractor.Scan(root, "1.99.9");
        Assert.Empty(groups);
        Directory.Delete(root, true);
    }

    [Fact]
    public void Scan_HashPrefixSubdir_MapsToObjectFile()
    {
        var root = PrepareRoot(out var versionId);
        var groups = McOstExtractor.Scan(root, versionId);
        foreach (var g in groups)
            foreach (var t in g.Tracks)
                Assert.True(File.Exists(t.FilePath));
        Directory.Delete(root, true);
    }

    private static string PrepareRoot(out string versionId)
    {
        var root = Path.Combine(Path.GetTempPath(), "mclcs_ost_" + Guid.NewGuid().ToString("N"));
        versionId = "1.20.1";
        var indexDir = Path.Combine(root, "assets", "indexes");
        var objDir = Path.Combine(root, "assets", "objects");
        Directory.CreateDirectory(indexDir);
        Directory.CreateDirectory(objDir);

        var entries = new (string key, string hash)[]
        {
            ("minecraft/sounds/music/menu1.ogg", "abC1"),
            ("minecraft/sounds/music/game2.ogg", "abC2"),
            ("minecraft/sounds/records/11.ogg", "cdD1"),
            ("minecraft/sounds/ambient/cave1.ogg", "efE1"),
            ("minecraft/sounds/weather/rain.ogg", "ghG1") // 非音乐前缀，应忽略
        };

        var obj = new JsonObject();
        foreach (var (key, hash) in entries)
        {
            var sub = Path.Combine(objDir, hash[..2]);
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, hash), "x");
            obj[key] = new JsonObject { ["hash"] = hash, ["size"] = 1 };
        }

        var doc = new JsonObject { ["objects"] = obj };
        File.WriteAllText(Path.Combine(indexDir, versionId + ".json"),
            doc.ToJsonString());
        return root;
    }

    // ---- MusicPlaylist 导航逻辑 ----

    private static MusicPlaylist ThreeTracks()
    {
        var p = new MusicPlaylist();
        p.Add(new Track { Path = "a", Title = "A" });
        p.Add(new Track { Path = "b", Title = "B" });
        p.Add(new Track { Path = "c", Title = "C" });
        return p;
    }

    [Fact]
    public void Sequential_AdvancesThenStops()
    {
        var p = ThreeTracks();
        p.Mode = PlayMode.Sequential;
        Assert.Equal("A", p.Next()!.Title);
        Assert.Equal("B", p.Next()!.Title);
        Assert.Equal("C", p.Next()!.Title);
        Assert.Null(p.Next()); // 末尾停止
    }

    [Fact]
    public void LoopAll_WrapsAround()
    {
        var p = ThreeTracks();
        p.Mode = PlayMode.LoopAll;
        Assert.Equal("A", p.Next()!.Title);
        Assert.Equal("B", p.Next()!.Title);
        Assert.Equal("C", p.Next()!.Title);
        Assert.Equal("A", p.Next()!.Title); // 回到开头
    }

    [Fact]
    public void LoopOne_StaysOnSameTrack()
    {
        var p = ThreeTracks();
        p.Mode = PlayMode.LoopOne;
        p.Select(1);
        Assert.Equal("B", p.Next()!.Title);
        Assert.Equal("B", p.Next()!.Title);
        Assert.Equal("C", p.Next(userTriggered: true)!.Title); // 用户手动切歌仍前进
    }

    [Fact]
    public void Shuffle_OneRoundNoRepeat()
    {
        var p = new MusicPlaylist(seed: 42);
        for (var i = 0; i < 12; i++) p.Add(new Track { Path = "t" + i, Title = "T" + i });
        p.Mode = PlayMode.Shuffle;

        var seen = new HashSet<string>();
        for (var i = 0; i < 12; i++)
        {
            var t = p.Next()!;
            Assert.True(seen.Add(t.Title), "一轮内不应重复：" + t.Title);
        }
        // 一轮覆盖全部 12 首后，下一首重新洗牌（随机模式不自然停止）
        Assert.Equal(12, seen.Count);
        Assert.NotNull(p.Next());
    }

    [Fact]
    public void Previous_ReverseOrder()
    {
        var p = ThreeTracks();
        p.Select(2);
        Assert.Equal("C", p.Current!.Title);
        Assert.Equal("B", p.Previous()!.Title);
        Assert.Equal("A", p.Previous()!.Title);
        Assert.Equal("C", p.Previous()!.Title); // 环形回退
    }

    [Fact]
    public void CycleMode_Transitions()
    {
        var p = ThreeTracks();
        Assert.Equal(PlayMode.LoopAll, p.Mode);
        Assert.Equal(PlayMode.LoopOne, p.CycleMode());
        Assert.Equal(PlayMode.Shuffle, p.CycleMode());
        Assert.Equal(PlayMode.Sequential, p.CycleMode());
        Assert.Equal(PlayMode.LoopAll, p.CycleMode());
    }

    [Fact]
    public void AddFolder_PicksSupportedExtensionsOnly()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mclcs_music_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "song.mp3"), "x");
        File.WriteAllText(Path.Combine(dir, "track.flac"), "x");
        File.WriteAllText(Path.Combine(dir, "clip.ogg"), "x");
        File.WriteAllText(Path.Combine(dir, "note.txt"), "x"); // 不支持

        var p = new MusicPlaylist();
        var added = p.AddFolder(dir, recursive: false);
        Assert.Equal(3, added);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void RemoveAt_KeepsCurrentValid()
    {
        var p = ThreeTracks();
        p.Select(1);
        Assert.True(p.RemoveAt(0));
        Assert.Equal("B", p.Current!.Title); // 删除前项后当前索引前移
        Assert.Equal(2, p.Count);
    }
}
