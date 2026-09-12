using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chert.Core.Theme;

namespace Chert.App.Themes;

/// <summary>
/// 从程序集内嵌资源加载 PNG UI 图标（编译进 DLL，输出目录不暴露原始文件）。
/// 资源路径：/Chert.App;component/Resources/Icons/{light|dark}/{token}.png
/// token 与 SidebarModel / DownloadCardItem / PngIcon 的键名一致（TDesign 图标集）。
/// 亮/暗随 ThemeManager.Current 自动切换。
/// </summary>
public static class IconImage
{
    /// <summary>取内嵌 PNG 图标；缺失或加载失败返回 null（由调用方回退到矢量）。</summary>
    /// <remarks>
    /// 当 <see cref="IconManager.HighDpi"/> 开启时优先加载 <c>@2x</c> 高清资源，
    /// 失败则回退到 1x；关闭时直接使用 1x。
    /// </remarks>
    public static ImageSource? Get(string? token, int size = 24)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var theme = ThemeManager.Current == ThemeType.Light ? "light" : "dark";

        if (IconManager.HighDpi)
        {
            var hi = TryLoad($"{theme}@2x", token);
            if (hi is not null) return hi;
        }
        return TryLoad(theme, token);
    }

    private static ImageSource? TryLoad(string folder, string token)
    {
        var uri = new Uri(
            $"pack://application:,,,/Chert.App;component/Resources/Icons/{folder}/{token}.png",
            UriKind.Absolute);
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.EndInit();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
