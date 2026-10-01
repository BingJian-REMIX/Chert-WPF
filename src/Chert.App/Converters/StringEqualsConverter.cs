using System.Globalization;
using System.Windows.Data;

namespace Chert.App.Converters;

/// <summary>
/// 值相等判定：把绑定值与 ConverterParameter 比较，返回 bool。
/// 用途：把「当前选中的界面风格 Id」映射成单选项的 IsChecked
/// （IsChecked="{Binding SelectedUiStyle, Converter={StaticResource StrEq}, ConverterParameter=glass}"）。
/// </summary>
public sealed class StringEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
