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

    /// <summary>为 true 时正在播放「收回」动画，UI 据此向右滑出；ToastService 随后将其移除。</summary>
    private bool _isLeaving;
    public bool IsLeaving { get => _isLeaving; set => SetField(ref _isLeaving, value); }

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

    /// <summary>当前生效的停留秒数（可在设置中调整，默认 5）。</summary>
    public static int DurationSeconds { get; set; } = DefaultSeconds;

    /// <summary>「收回」动画时长（需与 MainWindow.xaml 中 Storyboard 的 Duration 保持一致）。</summary>
    internal static readonly TimeSpan ExitAnimation = TimeSpan.FromSeconds(0.28);

    public static ObservableCollection<ToastItem> Items { get; } = new();

    /// <summary>
    /// 弹一条通知。<paramref name="seconds"/> 传 0 表示不自动消失（用户手动关）。
    /// 可在任意线程调用，内部会切回 UI 线程。
    /// </summary>
    public static ToastItem Show(
        string title, string message,
        ToastKind kind = ToastKind.Info,
        string? actionText = null, Action? action = null,
        int? seconds = null)
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
            // C5：超限时不要直接 Items.Remove —— 那样 IsLeaving 永远没置 true，
            // 退场 DataTrigger 不会触发，Toast 会「瞬间消失、没有收回动画」。
            // 改为走 Dismiss：IsLeaving=true → 播放向右收回动画 → 计时器到点移除。
            // 元素在动画播完前仍留在集合里（IsLeaving 已置位的那些不再计入），
            // 故这里用 while 最多挤出 1 条即可，不会死循环。
            while (Items.Count(v => !v.IsLeaving) >= MaxVisible)
                Dismiss(Items.First(x => !x.IsLeaving));
            Items.Add(item);

            int effective = seconds ?? DurationSeconds;
            if (effective <= 0) return;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(effective) };
            timer.Tick += (_, _) => Dismiss(item);
            item.Timer = timer;
            timer.Start();
        });

        return item;
    }

    public static void Dismiss(ToastItem item) => Invoke(() =>
    {
        if (item.IsLeaving) return;
        item.IsLeaving = true;
        item.Timer?.Stop();
        item.Timer = null;
        var t = new DispatcherTimer { Interval = ExitAnimation };
        t.Tick += (_, _) =>
        {
            t.Stop();
            Items.Remove(item);
        };
        t.Start();
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
