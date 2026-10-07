using QRCoder;

namespace Chert.App.Services;

/// <summary>
/// 二维码的 PNG 编码部分。刻意和 <see cref="QrCodeService"/> 分开：
/// 这里不碰 WPF，于是能在普通控制台里跑 ——
/// 「生成的码到底扫不扫得出来」这件事必须有办法离开 UI 验证，否则出了错没人发现。
/// </summary>
public static class QrCodePng
{
    /// <summary>深模块（不透明黑）—— 不跟随主题是有意的，见 <see cref="QrCodeService.Render"/>。</summary>
    public const byte DarkR = 0x00, DarkG = 0x00, DarkB = 0x00, DarkA = 0xFF;

    /// <summary>浅底（不透明白）。</summary>
    public const byte LightR = 0xFF, LightG = 0xFF, LightB = 0xFF, LightA = 0xFF;

    /// <summary>
    /// 生成时对齐到的大致宽度（像素）。这个值必须和界面上那个方框的实际尺寸一致：
    /// 两边不一致就会发生缩放，而缩放会吃掉模块边缘的细节。
    /// </summary>
    public const int TargetWidth = 320;

    /// <summary>
    /// 把文本编成二维码 PNG 字节。
    /// 用 <c>PngByteQRCode</c>：它自己拼 PNG，不碰 System.Drawing/GDI+，
    /// 因此没有 DPI 缩放与句柄泄漏这些事。
    /// </summary>
    /// <param name="text">要编码的文本。</param>
    /// <param name="pixelsPerModule">每模块的像素数；传 0（默认）则按 <see cref="TargetWidth"/> 自适应。</param>
    /// <returns>文本为空或编码失败时返回 null（二维码挂了不影响功能，长码还能手动复制）。</returns>
    public static byte[]? ToPngBytes(string? text, int pixelsPerModule = 0)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);

            // 为什么必须自适应：内容越长版本越高、模块越密。实测这段握手码是 69×69 模块（版本 13），
            // 固定按 8 像素一个模块生成会得到 552px 的图，界面上缩到 240px 后每个模块不到 3 像素 ——
            // 缩小时丢细节，手机就扫不出来了。让生成的尺寸与显示尺寸对上，反而比"高清大图"好扫。
            // 模块矩阵本身已经带静区（实测：这段码是 69×69，外面 4 圈全白），
            // 所以别再自己加一边——多算 8 个模块会让 scale 偏小，图糊。
            var modules = data.ModuleMatrix.Count;
            var scale = pixelsPerModule > 0
                ? pixelsPerModule
                : Math.Clamp((int)Math.Round((double)TargetWidth / modules), 2, 12);

            // drawQuietZones 对 PNG 渲染器不改变尺寸（静区已在矩阵里），留着是为了换渲染器时不漏掉
            return new PngByteQRCode(data).GetGraphic(
                scale,
                new[] { DarkR, DarkG, DarkB, DarkA },
                new[] { LightR, LightG, LightB, LightA },
                drawQuietZones: true);
        }
        catch
        {
            return null;
        }
    }
}
