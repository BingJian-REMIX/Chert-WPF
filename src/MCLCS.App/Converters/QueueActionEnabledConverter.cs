using System;
using System.Globalization;
using System.Windows.Data;

namespace MCLCS.App.Converters;

/// <summary>
/// 下载队列项的 暂停/取消 按钮可用性转换器。
/// ConverterParameter: "pause" 或 "cancel"；输入为队列项状态字符串。
/// </summary>
public sealed class QueueActionEnabledConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var status = value?.ToString() ?? "";
        var action = parameter?.ToString() ?? "";

        if (action == "pause")
            return status is "排队中" or "下载中";

        if (action == "cancel")
            return status is "排队中" or "下载中" or "已暂停";

        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
