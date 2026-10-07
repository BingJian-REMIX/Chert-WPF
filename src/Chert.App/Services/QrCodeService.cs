using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace Chert.App.Services;

/// <summary>
/// 把握手码画成二维码 —— 对方拿手机扫一下，比复制两百来个字符省事得多。
/// </summary>
public static class QrCodeService
{
    /// <summary>
    /// 渲染成 PNG 位图。
    /// <para>
    /// 颜色刻意固定成「深模块 + 浅底」而不跟随主题：
    /// 深色模式下的反色二维码有一部分扫码器认不出来，
    /// 而这里是**要被别人拍照**的图，可扫性优先于美观。
    /// </para>
    /// <para>
    /// 用的是 <c>PngByteQRCode</c>：它自己拼 PNG，不碰 System.Drawing/GDI+，
    /// 因此不会有 DPI 缩放、句柄泄漏这些事。
    /// </para>
    /// </summary>
    /// <param name="pixelsPerModule">每模块像素数；0 表示按显示区大小自适应（推荐）。</param>
    public static BitmapImage? Render(string? text, int pixelsPerModule = 0)
    {
        var png = QrCodePng.ToPngBytes(text, pixelsPerModule);
        if (png is null) return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = new MemoryStream(png);
            image.CacheOption = BitmapCacheOption.OnLoad;   // 读完就把流丢掉，别锁着
            image.EndInit();
            image.Freeze();                                  // 跨线程读（渲染线程）必备
            return image;
        }
        catch
        {
            // 二维码挂了不影响功能：长码文本框还能手动复制
            return null;
        }
    }
}
