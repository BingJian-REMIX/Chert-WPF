using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Chert.Core.Ai;

namespace Chert.App.Converters;

/// <summary>Ollama 服务状态 → 指示灯画刷：Running=绿，Starting=黄，NotRunning=红。</summary>
public class OllamaStatusToBrushConverter : IValueConverter
{
    public static readonly OllamaStatusToBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is OllamaServiceStatus s)
        {
            return s switch
            {
                OllamaServiceStatus.Running => new SolidColorBrush(Colors.LimeGreen),
                OllamaServiceStatus.Starting => new SolidColorBrush(Colors.Gold),
                _ => new SolidColorBrush(Colors.Red)
            };
        }
        return new SolidColorBrush(Colors.Red);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
