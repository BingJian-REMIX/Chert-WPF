using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Chert.App.Converters;

/// <summary>颜色字符串（#RRGGBB）→ SolidColorBrush（用于主题色预览）。</summary>
public class StringToBrushConverter : IValueConverter
{
    public static readonly StringToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string;
        if (string.IsNullOrWhiteSpace(s)) return new SolidColorBrush(Colors.Transparent);
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(s)); }
        catch { return new SolidColorBrush(Colors.Transparent); }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
