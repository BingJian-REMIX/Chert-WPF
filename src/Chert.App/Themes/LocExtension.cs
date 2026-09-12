using System.Windows;
using System.Windows.Markup;
using Chert.Core.Localization;

namespace Chert.App.Themes;

/// <summary>
/// WPF 本地化 MarkupExtension，配合 LocaleManager 实现运行时即时切换。
/// 用法：Text="{theme:Loc game.btn_start}" 或 Content="{theme:Loc tab.game}"
/// 支持格式参数：Text="{theme:Loc status.installed, Args='{}{0} versions'}"
/// </summary>
public class LocExtension : MarkupExtension
{
    private static readonly List<WeakReference<LocExtension>> _instances = new();
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

        // 注册自身以便语言切换时刷新
        lock (_instances)
        {
            _instances.Add(new WeakReference<LocExtension>(this));
            if (!_eventSubscribed)
            {
                _eventSubscribed = true;
                LocaleManager.LocaleChanged += OnLocaleChanged;
            }
        }

        // 如果目标是 DependencyObject，保存目标属性以便后续刷新
        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget pvt
            && pvt.TargetObject is DependencyObject target
            && pvt.TargetProperty is DependencyProperty dp)
        {
            _target = new WeakReference<DependencyObject>(target);
            _targetProperty = dp;
        }

        return GetValue();
    }

    private WeakReference<DependencyObject>? _target;
    private DependencyProperty? _targetProperty;

    private string GetValue()
    {
        if (!string.IsNullOrEmpty(Args))
            return LocaleManager.Tf(Key, Args);
        return LocaleManager.T(Key);
    }

    private static void OnLocaleChanged(string _)
    {
        lock (_instances)
        {
            // 清理失效引用并刷新所有活跃实例
            _instances.RemoveAll(wr => !wr.TryGetTarget(out LocExtension? _));

            foreach (var wr in _instances)
            {
                if (wr.TryGetTarget(out var ext) && ext._target != null && ext._target.TryGetTarget(out var target))
                {
                    // 在 UI 线程上更新
                    if (target.Dispatcher.CheckAccess())
                    {
                        target.SetValue(ext._targetProperty!, ext.GetValue());
                    }
                    else
                    {
                        target.Dispatcher.BeginInvoke(() =>
                        {
                            if (ext._target != null && ext._target.TryGetTarget(out var t))
                                t.SetValue(ext._targetProperty!, ext.GetValue());
                        });
                    }
                }
            }
        }
    }
}
