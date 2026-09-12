using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Chert.App.Converters;

/// <summary>bool → 颜色：true 用绿色，false 用红色（网络指示灯）。</summary>
public class BoolToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var ok = value is bool b && b;
        var color = ok ? Color.FromRgb(0x5B, 0xBF, 0x6A) : Color.FromRgb(0xE0, 0x53, 0x3A);
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
