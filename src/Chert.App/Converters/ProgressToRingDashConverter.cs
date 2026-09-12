using System;
using System.Globalization;
using System.Windows.Data;

namespace Chert.App.Converters;

/// <summary>
/// 把 0–100 的进度转为环形进度条的 <see cref="System.Windows.Shapes.Ellipse.StrokeDashArray"/> 虚线串。
/// 配合固定的环半径使用（ConverterParameter 传圆周长 = 2πr）。
/// 对齐 MCLCS-Linux 的细环形进度指示（bug.txt #7）。
/// </summary>
public class ProgressToRingDashConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double progress = 0;
        if (value is double d) progress = d;
        else if (value is int i) progress = i;
        progress = Math.Max(0, Math.Min(100, progress));

        double circumference = 56.55; // 默认对应半径 9（直径 18）
        if (parameter is double c && c > 0) circumference = c;
        else if (parameter is string s && double.TryParse(s, out var pc) && pc > 0) circumference = pc;

        var used = progress / 100.0 * circumference;
        return $"{used:F2} {circumference - used:F2}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
