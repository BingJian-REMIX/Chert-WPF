using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Chert.Core.Theme;
using Chert.Core.Toolbox;

namespace Chert.App.Converters;

/// <summary>日志级别 → 颜色（错误红 / 警告橙 / 调试灰 / 信息用主题前景）。</summary>
public class LogSeverityToColor : IValueConverter
{
    public static readonly LogSeverityToColor Instance = new();

    // 信息/默认级用主题前景画刷；缓存以避免每行重复查找，主题切换时失效重建。
    private static Brush? _infoBrush;
    private static readonly object _infoLock = new();

    static LogSeverityToColor()
    {
        // 主题切换后重新取色，保证亮/暗下都清晰可读（避免“黑底黑字”）。
        try { ThemeManager.OnThemeChanged += _ => { lock (_infoLock) _infoBrush = null; }; }
        catch { }
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var sev = value is LogSeverity s ? s : LogSeverity.Info;
        return sev switch
        {
            // 错误红 / 警告橙 / 调试亮灰：保持染色以便快速区分
            LogSeverity.Error => new SolidColorBrush(Color.FromRgb(0xE5, 0x4D, 0x42)),
            LogSeverity.Warn => new SolidColorBrush(Color.FromRgb(0xF0, 0xB4, 0x30)),
            LogSeverity.Debug => new SolidColorBrush(Color.FromRgb(0xB4, 0xBA, 0xC2)),
            // 信息/默认级：直接用主题前景画刷（亮/暗均清晰可读），避免回到 Binding.DoNothing
            // 后在某些环境下回退成系统默认黑字，造成“黑底黑字”。取不到时退回 DoNothing。
            _ => GetInfoBrush()
        };
    }

    private static object GetInfoBrush()
    {
        lock (_infoLock)
        {
            if (_infoBrush is null)
            {
                try
                {
                    if (Application.Current?.TryFindResource("PrimaryForeground") is Brush b)
                        _infoBrush = b;
                }
                catch { }
            }
        }
        return _infoBrush ?? (object)Binding.DoNothing;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
