using System.Windows.Controls;
using System.Windows.Data;
using Chert.App.Converters;

namespace Chert.App.ViewModels;

/// <summary>UI 辅助：暴露 bool → Visibility 转换器的静态实例，供 XAML 以 x:Static 直接绑定。</summary>
public static class VisibilityHelper
{
    /// <summary>bool → Visibility（true=Visible）。</summary>
    public static readonly IValueConverter BoolToVis = new BooleanToVisibilityConverter();

    /// <summary>
    /// bool → Visibility 取反（true=Collapsed）。
    /// WPF 内置的 <see cref="BooleanToVisibilityConverter"/> 没有 TrueValue / FalseValue 属性
    /// （那是 UWP 的 API），因此复用项目自带的 <see cref="InverseBoolToVisibilityConverter"/>。
    /// </summary>
    public static readonly IValueConverter BoolToVisInvert = InverseBoolToVisibilityConverter.Instance;
}
