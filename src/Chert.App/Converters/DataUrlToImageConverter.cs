using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Chert.App.Converters;

/// <summary>
/// 把 data URL（<c>data:image/png;base64,...</c>）或普通图片地址转成 <see cref="BitmapImage"/>。
///
/// <para><b>为什么需要它</b>：扫码登录接口的二维码由服务端直接给 data URL ——
/// Core 层不能依赖 WPF 的图像类型（那样就没法双端共用），所以只传字符串上来，
/// 由界面层解码。同一个 Core 返回结果在 Avalonia 端换另一个转换器即可。</para>
///
/// <para><b>失败策略</b>：任何异常都返回 <see cref="Binding.DoNothing"/> ——
/// 二维码解析失败最多是图不显示，不该把绑定系统拖崩（那样会把整个设置页炸掉）。</para>
/// </summary>
public sealed class DataUrlToImageConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s)) return Binding.DoNothing;

        try
        {
            if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = s.IndexOf(',');
                if (comma <= 0) return Binding.DoNothing;

                var meta = s[..comma];
                if (!meta.Contains("base64", StringComparison.OrdinalIgnoreCase)) return Binding.DoNothing;

                var bytes = System.Convert.FromBase64String(s[(comma + 1)..]);
                var img = new BitmapImage();
                // OnLoad：立刻读完再关流，否则 MemoryStream 被 using 掉后图片取不到像素
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = new MemoryStream(bytes);
                img.EndInit();
                img.Freeze();
                return img;
            }

            // 也允许直接给 http(s) 图片地址
            return new BitmapImage(new Uri(s));
        }
        catch
        {
            return Binding.DoNothing;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
