using System;
using System.Globalization;
using System.Windows.Data;

namespace Chert.App.Converters;

/// <summary>字符串相等比较：用于 RadioButton 与字符串属性（如 AiMode）双向绑定。
/// Convert: value == parameter ? true : false；ConvertBack: 选中时返回 parameter，未选中返回 DoNothing。</summary>
public class StringEqualityConverter : IValueConverter
{
    public static readonly StringEqualityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var a = value?.ToString() ?? "";
        var b = parameter?.ToString() ?? "";
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b) return parameter ?? "";
        return Binding.DoNothing;
    }
}
