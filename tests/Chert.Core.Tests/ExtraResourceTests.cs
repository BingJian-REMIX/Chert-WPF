using Chert.Core.Download;
using Chert.Core.Utils;
using Xunit;

namespace Chert.Core.Tests;

/// <summary>地图附加资源探测 + Markdown 纯文本化的纯函数自检。</summary>
public class ExtraResourceTests
{
    [Fact]
    public void Detect_ResourcePack_ByPackMcmetaAndAssets()
    {
        var names = new[] { "pack.mcmeta", "pack.png", "assets/minecraft/textures/block/stone.png" };
        Assert.Equal(ExtraResourceKind.ResourcePack, ExtraResourceInstaller.Detect(names));
    }

    [Fact]
    public void Detect_ShaderPack_ByShadersDir()
    {
        var names = new[] { "shaders/final.fsh", "shaders/gbuffers_terrain.vsh", "shaders/shaders.properties" };
        Assert.Equal(ExtraResourceKind.ShaderPack, ExtraResourceInstaller.Detect(names));
    }

    [Fact]
    public void Detect_ShaderPack_WinsOverPackMcmeta()
    {
        // 部分光影包也带 pack.mcmeta，shaders 目录优先级更高
        var names = new[] { "pack.mcmeta", "shaders/composite.fsh" };
        Assert.Equal(ExtraResourceKind.ShaderPack, ExtraResourceInstaller.Detect(names));
    }

    [Fact]
    public void Detect_DataPack_ByDataDirWithoutAssets()
    {
        var names = new[] { "pack.mcmeta", "data/mymap/functions/init.mcfunction" };
        Assert.Equal(ExtraResourceKind.DataPack, ExtraResourceInstaller.Detect(names));
    }

    [Fact]
    public void Detect_ResourcePack_WhenBothDataAndAssets()
    {
        // 同时带 assets 的按资源包处理（MC 会各取所需）
        var names = new[] { "pack.mcmeta", "data/x/tags/blocks/a.json", "assets/minecraft/lang/zh_cn.json" };
        Assert.Equal(ExtraResourceKind.ResourcePack, ExtraResourceInstaller.Detect(names));
    }

    [Fact]
    public void Detect_Container_ByInnerZips()
    {
        var names = new[] { "资源包.zip", "光影.zip", "readme.txt" };
        Assert.Equal(ExtraResourceKind.Container, ExtraResourceInstaller.Detect(names));
    }

    [Fact]
    public void Detect_Container_ByFolderStylePackages()
    {
        var names = new[]
        {
            "MyPack/pack.mcmeta",
            "MyPack/assets/minecraft/textures/a.png",
            "MyShader/shaders/final.fsh"
        };
        Assert.Equal(ExtraResourceKind.Container, ExtraResourceInstaller.Detect(names));
    }

    [Fact]
    public void Detect_Unknown_ForPlainFiles()
    {
        Assert.Equal(ExtraResourceKind.Unknown, ExtraResourceInstaller.Detect(new[] { "readme.txt", "封面.png" }));
        Assert.Equal(ExtraResourceKind.Unknown, ExtraResourceInstaller.Detect(System.Array.Empty<string>()));
    }

    [Fact]
    public void Detect_HandlesBackslashesAndLeadingSlash()
    {
        var names = new[] { "/pack.mcmeta", @"assets\minecraft\textures\x.png" };
        Assert.Equal(ExtraResourceKind.ResourcePack, ExtraResourceInstaller.Detect(names));
    }

    [Fact]
    public void SubPackagePrefixes_FindsFolderStylePackages()
    {
        var names = new[]
        {
            "MyPack/pack.mcmeta",
            "MyPack/assets/a.png",
            "MyShader/shaders/final.fsh",
            "readme.txt",
            "nested/deep/pack.mcmeta"   // 二级以下不算顶层子包
        };
        var prefixes = ExtraResourceInstaller.SubPackagePrefixes(names);

        Assert.Contains("MyPack/", prefixes);
        Assert.Contains("MyShader/", prefixes);
        Assert.DoesNotContain("nested/", prefixes);
        Assert.Equal(2, prefixes.Count);
    }

    [Fact]
    public void TargetDirFor_MapsKindToDirectory()
    {
        const string root = "/mc";
        Assert.EndsWith("resourcepacks", ExtraResourceInstaller.TargetDirFor(ExtraResourceKind.ResourcePack, root));
        Assert.EndsWith("shaderpacks", ExtraResourceInstaller.TargetDirFor(ExtraResourceKind.ShaderPack, root));
        Assert.EndsWith("datapacks", ExtraResourceInstaller.TargetDirFor(ExtraResourceKind.DataPack, root));
        Assert.EndsWith("extras", ExtraResourceInstaller.TargetDirFor(ExtraResourceKind.Unknown, root));
    }

    [Fact]
    public void SafeName_StripsInvalidCharsAndFallsBack()
    {
        Assert.Equal("附加资源", ExtraResourceInstaller.SafeName(null));
        Assert.Equal("附加资源", ExtraResourceInstaller.SafeName("   "));
        Assert.DoesNotContain("/", ExtraResourceInstaller.SafeName("a/b"));
        Assert.Equal(80, ExtraResourceInstaller.SafeName(new string('x', 200)).Length);
    }

    [Fact]
    public void InstallResult_SummaryGroupsByKind()
    {
        var result = new ExtraResourceInstallResult
        {
            Ok = true,
            Entries =
            {
                new ExtraResourceEntry { Name = "a", Kind = ExtraResourceKind.ResourcePack },
                new ExtraResourceEntry { Name = "b", Kind = ExtraResourceKind.ResourcePack },
                new ExtraResourceEntry { Name = "c", Kind = ExtraResourceKind.ShaderPack }
            }
        };

        Assert.Contains("资源包 ×2", result.Summary);
        Assert.Contains("光影 ×1", result.Summary);
        Assert.False(result.HasUnknown);
    }

    [Fact]
    public void ExtraDownloadItem_NullWhenNoAdditionalUrl()
    {
        var detail = new PixelMapDetail { Slug = "x", Title = "测试地图" };
        Assert.Null(PixelmapClient.ToExtraDownloadItem(detail, "/mc"));
        Assert.False(detail.HasAdditionalResources);
    }

    [Fact]
    public void ExtraDownloadItem_BuiltWhenUrlPresent()
    {
        var detail = new PixelMapDetail
        {
            Slug = "x",
            Title = "测试地图",
            AdditionalResourcesUrl = "https://goto.pixelmap.cc/files/extra-123.zip"
        };

        var item = PixelmapClient.ToExtraDownloadItem(detail, "/mc");
        Assert.NotNull(item);
        Assert.Contains("extras", item!.Destination);
        Assert.EndsWith("extra-123.zip", item.Destination);
    }

    [Fact]
    public void MarkdownText_StripsMarkupKeepsStructure()
    {
        var md = string.Join("\n",
            "# 标题",
            "",
            "这是**粗体**和 `代码` 与 [链接](https://example.com)。",
            "",
            "![封面](https://example.com/a.png)",
            "",
            "- 项目一",
            "- 项目二",
            "",
            "```csharp",
            "var x = 1;  // 代码块应被丢弃",
            "```",
            "",
            "结尾。");

        var text = MarkdownText.ToPlainText(md);

        Assert.Contains("标题", text);
        Assert.Contains("这是粗体和 代码 与 链接。", text);
        Assert.Contains("· 项目一", text);
        Assert.DoesNotContain("**", text);
        Assert.DoesNotContain("https://example.com", text);
        Assert.DoesNotContain("var x = 1", text);
        Assert.DoesNotContain("```", text);
    }

    [Fact]
    public void MarkdownText_TruncatesAtMaxLength()
    {
        var text = MarkdownText.ToPlainText(new string('字', 100), maxLength: 20);
        Assert.Equal(21, text.Length);          // 20 字 + 省略号
        Assert.EndsWith("…", text);
    }

    [Fact]
    public void MarkdownText_EmptyInputReturnsEmpty()
    {
        Assert.Equal("", MarkdownText.ToPlainText(null));
        Assert.Equal("", MarkdownText.ToPlainText("   \n  "));
    }

    [Fact]
    public void MapDetail_DescriptionFallsBackToSummary()
    {
        var detail = new PixelMapDetail { Summary = "一句话简介", ContentMarkdown = null };
        Assert.Equal("一句话简介", detail.DescriptionText);

        detail.ContentMarkdown = "## 正文标题\n正文内容";
        Assert.Contains("正文内容", detail.DescriptionText);
    }

    [Fact]
    public void MapDetail_RatingSummary()
    {
        Assert.Equal("暂无评分", new PixelMapDetail().RatingSummary);

        var rated = new PixelMapDetail { RatingAverage = 4.75, RatingCount = 12 };
        Assert.Contains("4.8", rated.RatingSummary);
        Assert.Contains("12", rated.RatingSummary);
    }
}
