using System;
using System.Globalization;
using System.Windows.Data;

namespace Chert.App.Converters;

/// <summary>
/// 下载队列项 暂停 / 取消 / 继续 / 重试 按钮「为什么是灰的」提示。
/// ConverterParameter 与 <see cref="QueueActionEnabledConverter"/> 一致（pause / cancel / resume / retry）。
/// 按钮可用时返回 null（不显示 ToolTip），不可用时返回原因。
/// 此前按钮直接灰掉且没有任何解释，用户只能猜。
/// </summary>
public sealed class QueueActionHintConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var status = value?.ToString() ?? "";
        var action = parameter?.ToString() ?? "";

        bool enabled = action switch
        {
            "pause" => status is "排队中" or "下载中",
            "cancel" => status is "排队中" or "下载中" or "已暂停",
            "resume" => status is "已暂停",
            "retry" => status is "失败" or "已取消",
            _ => false
        };
        if (enabled) return null;

        if (status is "已完成") return "该任务已完成，无需操作";
        if (status is "失败" or "已取消") return action is "pause" or "cancel" ? "任务已结束，可重试" : null;

        return action switch
        {
            "pause" => status is "已暂停" ? "已经是暂停状态" : "只有排队中 / 下载中的任务可以暂停",
            "cancel" => "只有未结束的任务可以取消",
            "resume" => "只有暂停中的任务可以继续",
            "retry" => "只有失败 / 已取消的任务可以重试",
            _ => null
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
