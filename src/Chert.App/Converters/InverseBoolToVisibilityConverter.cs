using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Chert.App.Converters;

/// <summary>bool → Visibility 的取反版本：true 显示 Collapsed，false 显示 Visible。
/// 用于下载页：地图专属筛选在「非地图」副标签时隐藏。</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public static readonly InverseBoolToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value is bool v && v;
        return b ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility v && v == Visibility.Visible
            ? false
            : true;
}
