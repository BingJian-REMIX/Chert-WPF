namespace Chert.Core.Toolbox;

/// <summary>皮肤模型。</summary>
public enum SkinModel
{
    /// <summary>经典（Steve），手臂 4px 宽。</summary>
    Classic,
    /// <summary>纤细（Alex），手臂 3px 宽。</summary>
    Slim
}

/// <summary>皮肤贴图上的一个矩形区域。</summary>
public sealed class SkinRegion
{
    public SkinRegion(string name, int x, int y, int width, int height, bool overlay = false)
    {
        Name = name;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        IsOverlay = overlay;
    }

    public string Name { get; }
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }

    /// <summary>是否为第二层（帽子 / 外套）。</summary>
    public bool IsOverlay { get; }

    public int Right => X + Width;
    public int Bottom => Y + Height;
    public int Area => Width * Height;

    /// <summary>点是否落在该区域内。</summary>
    public bool Contains(int px, int py) => px >= X && px < Right && py >= Y && py < Bottom;

    public override string ToString() => $"{Name} ({X},{Y} {Width}×{Height})";
}

/// <summary>皮肤校验结果。</summary>
public sealed class SkinValidation
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>是否为旧版 64×32 皮肤（需转换）。</summary>
    public bool IsLegacy { get; init; }

    public static SkinValidation Fail(string error) => new() { Ok = false, Error = error };
}

/// <summary>
/// 皮肤编辑器的区域映射与校验（工具箱开发工具）。
/// 只处理坐标 / 尺寸 / 模型这类纯逻辑，像素绘制由界面层完成。
/// </summary>
public static class SkinEditor
{
    public const int SkinWidth = 64;
    public const int SkinHeight = 64;
    public const int LegacyHeight = 32;

    /// <summary>PNG 文件头魔数。</summary>
    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>经典模型的区域表（不含第二层）。</summary>
    public static IReadOnlyList<SkinRegion> ClassicRegions { get; } = BuildRegions(SkinModel.Classic, false);

    /// <summary>纤细模型的区域表（不含第二层）。</summary>
    public static IReadOnlyList<SkinRegion> SlimRegions { get; } = BuildRegions(SkinModel.Slim, false);

    /// <summary>取某模型的区域表。</summary>
    public static IReadOnlyList<SkinRegion> RegionsOf(SkinModel model, bool includeOverlay = true) =>
        includeOverlay ? BuildRegions(model, true)
                       : (model == SkinModel.Slim ? SlimRegions : ClassicRegions);

    /// <summary>手臂宽度：经典 4px，纤细 3px。</summary>
    public static int ArmWidth(SkinModel model) => model == SkinModel.Slim ? 3 : 4;

    /// <summary>命中测试：返回坐标落在的区域（优先返回非第二层）。</summary>
    public static SkinRegion? HitTest(SkinModel model, int x, int y)
    {
        var regions = BuildRegions(model, true);
        return regions.FirstOrDefault(r => !r.IsOverlay && r.Contains(x, y))
               ?? regions.FirstOrDefault(r => r.Contains(x, y));
    }

    /// <summary>
    /// 从 PNG 头部读取宽高并校验是否为合法皮肤。不依赖图像库，只解析 IHDR。
    /// </summary>
    public static SkinValidation Validate(byte[]? pngBytes)
    {
        if (pngBytes is null || pngBytes.Length < 24) return SkinValidation.Fail("文件过小，不是有效的 PNG");
        for (var i = 0; i < PngMagic.Length; i++)
            if (pngBytes[i] != PngMagic[i])
                return SkinValidation.Fail("不是 PNG 文件");

        // IHDR：8 字节签名 + 4 长度 + 4 类型 + 宽(4) + 高(4)
        var width = ReadInt32BE(pngBytes, 16);
        var height = ReadInt32BE(pngBytes, 20);

        if (width != SkinWidth || (height != SkinHeight && height != LegacyHeight))
            return new SkinValidation
            {
                Ok = false,
                Error = $"皮肤尺寸应为 64×64（或旧版 64×32），当前 {width}×{height}",
                Width = width,
                Height = height
            };

        return new SkinValidation
        {
            Ok = true,
            Width = width,
            Height = height,
            IsLegacy = height == LegacyHeight
        };
    }

    /// <summary>校验文件；读不到返回失败。</summary>
    public static SkinValidation ValidateFile(string path)
    {
        try
        {
            return File.Exists(path) ? Validate(File.ReadAllBytes(path)) : SkinValidation.Fail("文件不存在");
        }
        catch (Exception ex)
        {
            return SkinValidation.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 经典 ↔ 纤细转换时需要处理的列偏移：
    /// 纤细模型手臂少 1px，转换时右臂 / 左臂各需裁剪或补齐一列。
    /// 返回受影响的区域名。
    /// </summary>
    public static IReadOnlyList<string> ArmRegionsAffectedByModelSwitch() => new[]
    {
        "右臂-正面", "右臂-背面", "右臂-右侧", "右臂-左侧", "右臂-顶面", "右臂-底面",
        "左臂-正面", "左臂-背面", "左臂-右侧", "左臂-左侧", "左臂-顶面", "左臂-底面"
    };

    /// <summary>切换模型（经典 ↔ 纤细）。</summary>
    public static SkinModel Toggle(SkinModel model) =>
        model == SkinModel.Classic ? SkinModel.Slim : SkinModel.Classic;

    public static string ModelText(SkinModel model) => model == SkinModel.Slim ? "纤细（Alex）" : "经典（Steve）";

    private static List<SkinRegion> BuildRegions(SkinModel model, bool includeOverlay)
    {
        var aw = ArmWidth(model);
        var list = new List<SkinRegion>
        {
            // 头
            new("头-正面", 8, 8, 8, 8),
            new("头-背面", 24, 8, 8, 8),
            new("头-右侧", 0, 8, 8, 8),
            new("头-左侧", 16, 8, 8, 8),
            new("头-顶面", 8, 0, 8, 8),
            new("头-底面", 16, 0, 8, 8),

            // 身体
            new("身体-正面", 20, 20, 8, 12),
            new("身体-背面", 32, 20, 8, 12),
            new("身体-右侧", 16, 20, 4, 12),
            new("身体-左侧", 28, 20, 4, 12),
            new("身体-顶面", 20, 16, 8, 4),
            new("身体-底面", 28, 16, 8, 4),

            // 右臂
            new("右臂-正面", 44, 20, aw, 12),
            new("右臂-背面", 44 + aw + 4, 20, aw, 12),
            new("右臂-右侧", 40, 20, 4, 12),
            new("右臂-左侧", 44 + aw, 20, 4, 12),
            new("右臂-顶面", 44, 16, aw, 4),
            new("右臂-底面", 44 + aw, 16, aw, 4),

            // 左臂
            new("左臂-正面", 36, 52, aw, 12),
            new("左臂-背面", 36 + aw + 4, 52, aw, 12),
            new("左臂-右侧", 32, 52, 4, 12),
            new("左臂-左侧", 36 + aw, 52, 4, 12),
            new("左臂-顶面", 36, 48, aw, 4),
            new("左臂-底面", 36 + aw, 48, aw, 4),

            // 右腿
            new("右腿-正面", 4, 20, 4, 12),
            new("右腿-背面", 12, 20, 4, 12),
            new("右腿-右侧", 0, 20, 4, 12),
            new("右腿-左侧", 8, 20, 4, 12),
            new("右腿-顶面", 4, 16, 4, 4),
            new("右腿-底面", 8, 16, 4, 4),

            // 左腿
            new("左腿-正面", 20, 52, 4, 12),
            new("左腿-背面", 28, 52, 4, 12),
            new("左腿-右侧", 16, 52, 4, 12),
            new("左腿-左侧", 24, 52, 4, 12),
            new("左腿-顶面", 20, 48, 4, 4),
            new("左腿-底面", 24, 48, 4, 4)
        };

        if (includeOverlay)
        {
            list.AddRange(new[]
            {
                new SkinRegion("帽子-正面", 40, 8, 8, 8, overlay: true),
                new SkinRegion("帽子-背面", 56, 8, 8, 8, overlay: true),
                new SkinRegion("帽子-右侧", 32, 8, 8, 8, overlay: true),
                new SkinRegion("帽子-左侧", 48, 8, 8, 8, overlay: true),
                new SkinRegion("帽子-顶面", 40, 0, 8, 8, overlay: true),
                new SkinRegion("帽子-底面", 48, 0, 8, 8, overlay: true),
                new SkinRegion("外套-正面", 20, 36, 8, 12, overlay: true),
                new SkinRegion("外套-背面", 32, 36, 8, 12, overlay: true)
            });
        }

        return list;
    }

    private static int ReadInt32BE(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}
