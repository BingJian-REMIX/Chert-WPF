using System.Collections.ObjectModel;
using System.Windows.Media;

namespace MCLCS.App.ViewModels;

/// <summary>皮肤编辑器的一个可编辑面（6 部位 × 6 面 = 36 面）。</summary>
public class SkinFace
{
    public string PartName { get; init; } = "";
    public string FaceName { get; init; } = "";
    public string Display => $"{PartName}·{FaceName}";
    public int SrcX { get; init; }
    public int SrcY { get; init; }
    public int W { get; init; }
    public int H { get; init; }

    /// <summary>是否镜像于另一个面（对称绘制用）。</summary>
    public string? MirrorOf { get; set; }

    public bool IsMirror => MirrorOf is not null;
}

/// <summary>皮肤的身体部位分组。</summary>
public class SkinPart
{
    public string Name { get; init; } = "";
    public ObservableCollection<SkinFace> Faces { get; } = new();
}

/// <summary>
/// Minecraft 64x64 皮肤布局的 36 面 UV 映射（规格 2.3 面板 13）。
/// 坐标原点为左上，单位为像素。
/// </summary>
public static class SkinLayout
{
    public static readonly List<SkinPart> Parts = new();

    static SkinLayout()
    {
        var head = AddPart("头部");
        AddFace(head, "正面",  8,  8, 8, 8);
        AddFace(head, "背面", 24,  8, 8, 8);
        AddFace(head, "右面",  0,  8, 8, 8);
        AddFace(head, "左面", 16,  8, 8, 8);
        AddFace(head, "顶面",  8,  0, 8, 8);
        AddFace(head, "底面", 16,  0, 8, 8);

        var body = AddPart("身体");
        AddFace(body, "正面", 20, 16, 8, 12);
        AddFace(body, "背面", 32, 16, 8, 12);
        AddFace(body, "右面", 16, 16, 4, 12);
        AddFace(body, "左面", 28, 16, 4, 12);
        AddFace(body, "顶面", 20, 28, 8, 4);
        AddFace(body, "底面", 28, 28, 8, 4);

        var ra = AddPart("右臂");
        AddFace(ra, "正面", 44, 16, 4, 12, mirror: "左臂·正面");
        AddFace(ra, "背面", 52, 16, 4, 12, mirror: "左臂·背面");
        AddFace(ra, "右面", 40, 16, 4, 12, mirror: "左臂·左面");
        AddFace(ra, "左面", 48, 16, 4, 12, mirror: "左臂·右面");
        AddFace(ra, "顶面", 44, 28, 4, 4,  mirror: "左臂·顶面");
        AddFace(ra, "底面", 48, 28, 4, 4,  mirror: "左臂·底面");

        var la = AddPart("左臂");
        AddFace(la, "正面", 36, 48, 4, 12, mirror: "右臂·正面");
        AddFace(la, "背面", 44, 48, 4, 12, mirror: "右臂·背面");
        AddFace(la, "右面", 40, 48, 4, 12, mirror: "右臂·左面");
        AddFace(la, "左面", 32, 48, 4, 12, mirror: "右臂·右面");
        AddFace(la, "顶面", 36, 60, 4, 4,  mirror: "右臂·顶面");
        AddFace(la, "底面", 40, 60, 4, 4,  mirror: "右臂·底面");

        var rl = AddPart("右腿");
        AddFace(rl, "正面",  4, 16, 4, 12, mirror: "左腿·正面");
        AddFace(rl, "背面", 12, 16, 4, 12, mirror: "左腿·背面");
        AddFace(rl, "右面",  0, 16, 4, 12, mirror: "左腿·左面");
        AddFace(rl, "左面",  8, 16, 4, 12, mirror: "左腿·右面");
        AddFace(rl, "顶面",  4, 28, 4, 4,  mirror: "左腿·顶面");
        AddFace(rl, "底面",  8, 28, 4, 4,  mirror: "左腿·底面");

        var ll = AddPart("左腿");
        AddFace(ll, "正面", 20, 48, 4, 12, mirror: "右腿·正面");
        AddFace(ll, "背面", 28, 48, 4, 12, mirror: "右腿·背面");
        AddFace(ll, "右面", 16, 48, 4, 12, mirror: "右腿·左面");
        AddFace(ll, "左面", 24, 48, 4, 12, mirror: "右腿·右面");
        AddFace(ll, "顶面", 20, 60, 4, 4,  mirror: "右腿·顶面");
        AddFace(ll, "底面", 24, 60, 4, 4,  mirror: "右腿·底面");

        // —— 第二层（overlay）：帽子 / 外套 / 袖子 / 裤子 ——
        // 坐标与 SkinModel3D 中 Overlay UV 完全一致，确保 2D 绘制能正确映射到 3D 双层预览。
        // 这些区域本就在同一个 64x64 _pixels 缓冲里，绘制逻辑（Paint/FloodFill/Mirror）无需改动。
        var hat = AddPart("帽子");
        AddFace(hat, "正面", 40, 8, 8, 8);
        AddFace(hat, "背面", 56, 8, 8, 8);
        AddFace(hat, "右面", 32, 8, 8, 8);
        AddFace(hat, "左面", 48, 8, 8, 8);
        AddFace(hat, "顶面", 40, 0, 8, 8);
        AddFace(hat, "底面", 48, 0, 8, 8);

        var jacket = AddPart("外套");
        AddFace(jacket, "正面", 20, 36, 8, 12);
        AddFace(jacket, "背面", 32, 36, 8, 12);
        AddFace(jacket, "右面", 28, 36, 4, 12);
        AddFace(jacket, "左面", 16, 36, 4, 12);
        AddFace(jacket, "顶面", 20, 32, 8, 4);
        AddFace(jacket, "底面", 28, 32, 8, 4);

        var rs = AddPart("右臂袖");
        AddFace(rs, "正面", 44, 36, 4, 12, mirror: "左臂袖·正面");
        AddFace(rs, "背面", 52, 36, 4, 12, mirror: "左臂袖·背面");
        AddFace(rs, "右面", 48, 36, 4, 12, mirror: "左臂袖·左面");
        AddFace(rs, "左面", 40, 36, 4, 12, mirror: "左臂袖·右面");
        AddFace(rs, "顶面", 44, 32, 4, 4, mirror: "左臂袖·顶面");
        AddFace(rs, "底面", 48, 32, 4, 4, mirror: "左臂袖·底面");

        var ls = AddPart("左臂袖");
        AddFace(ls, "正面", 52, 52, 4, 12, mirror: "右臂袖·正面");
        AddFace(ls, "背面", 60, 52, 4, 12, mirror: "右臂袖·背面");
        AddFace(ls, "右面", 56, 52, 4, 12, mirror: "右臂袖·左面");
        AddFace(ls, "左面", 48, 52, 4, 12, mirror: "右臂袖·右面");
        AddFace(ls, "顶面", 52, 48, 4, 4, mirror: "右臂袖·顶面");
        AddFace(ls, "底面", 56, 48, 4, 4, mirror: "右臂袖·底面");

        var rp = AddPart("右腿裤");
        AddFace(rp, "正面", 4, 36, 4, 12, mirror: "左腿裤·正面");
        AddFace(rp, "背面", 12, 36, 4, 12, mirror: "左腿裤·背面");
        AddFace(rp, "右面", 8, 36, 4, 12, mirror: "左腿裤·左面");
        AddFace(rp, "左面", 0, 36, 4, 12, mirror: "左腿裤·右面");
        AddFace(rp, "顶面", 4, 32, 4, 4, mirror: "左腿裤·顶面");
        AddFace(rp, "底面", 8, 32, 4, 4, mirror: "左腿裤·底面");

        var lp = AddPart("左腿裤");
        AddFace(lp, "正面", 4, 52, 4, 12, mirror: "右腿裤·正面");
        AddFace(lp, "背面", 12, 52, 4, 12, mirror: "右腿裤·背面");
        AddFace(lp, "右面", 8, 52, 4, 12, mirror: "右腿裤·左面");
        AddFace(lp, "左面", 0, 52, 4, 12, mirror: "右腿裤·右面");
        AddFace(lp, "顶面", 4, 48, 4, 4, mirror: "右腿裤·顶面");
        AddFace(lp, "底面", 8, 48, 4, 4, mirror: "右腿裤·底面");
    }

    private static SkinPart AddPart(string name)
    {
        var part = new SkinPart { Name = name };
        Parts.Add(part);
        return part;
    }

    private static void AddFace(SkinPart part, string faceName, int x, int y, int w, int h, string? mirror = null)
    {
        part.Faces.Add(new SkinFace
        {
            PartName = part.Name,
            FaceName = faceName,
            SrcX = x, SrcY = y,
            W = w, H = h,
            MirrorOf = mirror
        });
    }

    /// <summary>按部位名和面名查找 SkinFace。</summary>
    public static SkinFace? Find(string partName, string faceName) =>
        Parts.SelectMany(p => p.Faces).FirstOrDefault(f =>
            f.PartName == partName && f.FaceName == faceName);

    /// <summary>找到指定面的镜像面。</summary>
    public static SkinFace? Mirror(SkinFace face)
    {
        if (face.MirrorOf is null) return null;
        var parts = face.MirrorOf.Split('·');
        return parts.Length == 2 ? Find(parts[0], parts[1]) : null;
    }

    /// <summary>在 64x64 纹理上，位于某一面的镜像位置的像素坐标。</summary>
    public static (int x, int y) MirrorPixel(int x, int y, SkinFace srcFace)
    {
        var dst = Mirror(srcFace);
        if (dst is null) return (x, y);
        // 计算在源面内的相对坐标
        var rx = x - srcFace.SrcX;
        var ry = y - srcFace.SrcY;
        // 镜像到目标面的对应位置（水平翻转）
        var mx = dst.SrcX + (srcFace.W - 1 - rx);
        var my = dst.SrcY + ry;
        return (mx, my);
    }
}
