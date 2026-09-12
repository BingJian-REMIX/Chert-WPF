using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Chert.App.Converters;

/// <summary>字符串非空 → Visible，空白 → Collapsed。
/// 用于地图卡片的浏览量、详情窗的提示行等"有内容才占位"的场景。</summary>
public class NonEmptyToVisibilityConverter : IValueConverter
{
    public static readonly NonEmptyToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
