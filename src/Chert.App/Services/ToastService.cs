using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Chert.Core.Mvvm;

namespace Chert.App.Services;

/// <summary>Toast 的语气（决定左侧色条颜色）。</summary>
public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>右下角的一条非阻塞通知。</summary>
public class ToastItem : ObservableObject
{
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public ToastKind Kind { get; init; } = ToastKind.Info;

    /// <summary>「查看详情」的文案；为空时不显示该按钮。</summary>
    public string? ActionText { get; init; }

    /// <summary>点「查看详情」时执行。</summary>
    public Action? Action { get; init; }

    /// <summary>色条颜色（跟随 Kind）。</summary>
    public string AccentColor => Kind switch
    {
        ToastKind.Success => "#4CAF50",
        ToastKind.Warning => "#FF9800",
        ToastKind.Error => "#E74C3C",
        _ => "#2196F3"
    };

    public bool HasAction => !string.IsNullOrWhiteSpace(ActionText) && Action is not null;

    public ICommand ActionCommand { get; }
    public ICommand CloseCommand { get; }

    internal DispatcherTimer? Timer { get; set; }

    public ToastItem()
    {
        ActionCommand = new RelayCommand(_ =>
        {
            try { Action?.Invoke(); }
            finally { ToastService.Dismiss(this); }
        });
        CloseCommand = new RelayCommand(_ => ToastService.Dismiss(this));
    }
}

/// <summary>
/// 全局 Toast 通知（规格 2.3-16：右下角非阻塞通知，5 秒后自动消失，可查看详情）。
/// MainWindow 里有一个 ItemsControl 绑定到 <see cref="Items"/>，因此任何地方都能直接 Show。
/// </summary>
public static class ToastService
{
    /// <summary>同屏最多堆叠几条，超出时挤掉最早的一条。</summary>
    public const int MaxVisible = 4;

    /// <summary>默认停留秒数（规格要求 5 秒）。</summary>
    public const int DefaultSeconds = 5;

    public static ObservableCollection<ToastItem> Items { get; } = new();

    /// <summary>
    /// 弹一条通知。<paramref name="seconds"/> 传 0 表示不自动消失（用户手动关）。
    /// 可在任意线程调用，内部会切回 UI 线程。
    /// </summary>
    public static ToastItem Show(
        string title, string message,
        ToastKind kind = ToastKind.Info,
        string? actionText = null, Action? action = null,
        int seconds = DefaultSeconds)
    {
        var item = new ToastItem
        {
            Title = title,
            Message = message,
            Kind = kind,
            ActionText = actionText,
            Action = action
        };

        Invoke(() =>
        {
            while (Items.Count >= MaxVisible) Dismiss(Items[0]);
            Items.Add(item);

            if (seconds <= 0) return;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += (_, _) => Dismiss(item);
            item.Timer = timer;
            timer.Start();
        });

        return item;
    }

    public static void Dismiss(ToastItem item) => Invoke(() =>
    {
        item.Timer?.Stop();
        item.Timer = null;
        Items.Remove(item);
    });

    public static void ClearAll() => Invoke(() =>
    {
        foreach (var i in Items.ToList()) i.Timer?.Stop();
        Items.Clear();
    });

    private static void Invoke(Action action)
    {
        var app = Application.Current;
        if (app is null) { action(); return; }

        if (app.Dispatcher.CheckAccess()) action();
        else app.Dispatcher.Invoke(action);
    }
}
