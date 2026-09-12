using System.Windows;
using System.Windows.Controls;

namespace Chert.App.Services;

/// <summary>
/// 大页导航（bug #10）：版本列表 / 版本设置作为覆盖在内容区之上的「大页」显示，
/// 而非四色索引贴（工具栏）里的子面板。MainWindow 注册 Show/Close 处理器，
/// 各页面通过 <see cref="Close"/> 回到上一页。解耦 ViewModel 与 MainWindow。
/// </summary>
public static class BigPageNavigator
{
    public static Action<FrameworkElement>? ShowHandler;
    public static Action? CloseHandler;

    public static void Show(FrameworkElement page) => ShowHandler?.Invoke(page);
    public static void Close() => CloseHandler?.Invoke();
}
