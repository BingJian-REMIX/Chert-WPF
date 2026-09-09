using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MCLCS.App.Converters;

/// <summary>字符串不等于指定参数 → Visible，否则 Collapsed（用于按动作类型显隐编辑项）。</summary>
public class StringNotEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, System.Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string;
        var p = parameter as string;
        return !string.Equals(s, p, System.StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, System.Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
