using System;
using System.Globalization;
using System.Windows.Data;

namespace Chert.App.Converters;

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

        // 「继续」：只有暂停中的项能恢复（此前队列没有恢复入口，暂停后只能取消重下）
        if (action == "resume")
            return status is "已暂停";

        // 「重试」：失败或被取消的项可以再来一次（配合断点续传，不会从 0 开始）
        if (action == "retry")
            return status is "失败" or "已取消";

        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
