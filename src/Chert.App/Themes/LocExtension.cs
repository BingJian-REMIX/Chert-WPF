using System.Windows;
using System.Windows.Markup;
using Chert.Core.Localization;

namespace Chert.App.Themes;

/// <summary>
/// WPF 本地化 MarkupExtension，配合 LocaleManager 实现运行时即时切换。
/// 用法：Text="{theme:Loc game.btn_start}" 或 Content="{theme:Loc tab.game}"
/// 支持格式参数：Text="{theme:Loc status.installed, Args='某个值'}"
/// </summary>
public class LocExtension : MarkupExtension
{
    /// <summary>运行时切换时需要刷新的目标条目。Key/Args/Entry 为强引用（条目本身很小，且目标销毁即从列表移除）；
    /// 目标 DependencyObject 用弱引用，靠视觉树存活——窗口/控件销毁后自动失效并被清理，避免内存泄漏与陈旧刷新。</summary>
    private sealed class Entry
    {
        public string Key = "";
        public string? Args;
        public WeakReference<DependencyObject> Target = new WeakReference<DependencyObject>(null!);
        public DependencyProperty? Property;
    }

    private static readonly List<Entry> _entries = new();
    private static bool _eventSubscribed;

    public string Key { get; set; } = "";
    public string? Args { get; set; }

    public LocExtension() { }

    public LocExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
            return "";

        // 仅当目标是 DependencyObject + DependencyProperty 时注册刷新（其余情况一次性求值即可）
        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget pvt
            && pvt.TargetObject is DependencyObject target
            && pvt.TargetProperty is DependencyProperty dp)
        {
            lock (_entries)
            {
                _entries.Add(new Entry
                {
                    Key = Key,
                    Args = Args,
                    Target = new WeakReference<DependencyObject>(target),
                    Property = dp
                });
                if (!_eventSubscribed)
                {
                    _eventSubscribed = true;
                    LocaleManager.LocaleChanged += OnLocaleChanged;
                }
            }
        }

        return GetValue();
    }

    private string GetValue()
    {
        if (!string.IsNullOrEmpty(Args))
            return LocaleManager.Tf(Key, Args);
        return LocaleManager.T(Key);
    }

    private static void OnLocaleChanged(string _)
    {
        lock (_entries)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (!entry.Target.TryGetTarget(out var target) || target is null || entry.Property is null)
                {
                    // 目标已销毁（窗口/控件关闭），移除条目
                    _entries.RemoveAt(i);
                    continue;
                }

                var value = string.IsNullOrEmpty(entry.Args)
                    ? LocaleManager.T(entry.Key)
                    : LocaleManager.Tf(entry.Key, entry.Args);

                if (target.CheckAccess())
                {
                    target.SetValue(entry.Property, value);
                }
                else
                {
                    target.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (entry.Target.TryGetTarget(out var t) && entry.Property is not null)
                        {
                            var v = string.IsNullOrEmpty(entry.Args)
                                ? LocaleManager.T(entry.Key)
                                : LocaleManager.Tf(entry.Key, entry.Args);
                            t.SetValue(entry.Property, v);
                        }
                    }));
                }
            }
        }
    }
}
