using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Chert.App.ViewModels;
using Chert.Core.Servers;

namespace Chert.App.Views;

public partial class GameView : UserControl
{
    public GameView()
    {
        InitializeComponent();
        DataContext = new GameViewModel();

        // problem3：横向卡片流滚轮支持。
        //   WPF 的 ScrollViewer 滚轮默认只作用于垂直方向，横向容器里滚轮完全无效 ——
        //   用户的体感是「必须把鼠标移到最左/最右露出滚动条才能拖」。
        //   这里在页根统一挂 PreviewMouseWheel（Preview 隧道能先于子元素的
        //   ItemsControl / 卡片 Button 拿到事件），命中横向 ScrollViewer 就换算偏移。
        PreviewMouseWheel += GameView_PreviewMouseWheel;
    }

    /// <summary>
    /// 把垂直滚轮换算为横向偏移，作用于页内所有
    /// <c>HorizontalScrollViewerStyle</c> 的卡片流（局域网 / 服务器 / 推荐 / 版本）。
    /// </summary>
    private void GameView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // 命中最近的祖先横向 ScrollViewer；不在卡片流里则不拦截，交给外层页面正常处理。
        var sv = FindAncestorHorizontalScrollViewer(e.OriginalSource as DependencyObject);
        if (sv is null || sv.ScrollableWidth <= 0) return;

        // 一格滚轮 ≈ 3 行的位移，观感更跟手
        const double step = 48;
        var next = sv.HorizontalOffset - Math.Sign(e.Delta) * step;
        sv.ScrollToHorizontalOffset(Math.Max(0, Math.Min(next, sv.ScrollableWidth)));

        // 标记已处理：否则事件继续冒泡，页面外层的 ScrollViewer 也会跟着动。
        e.Handled = true;
    }

    /// <summary>从事件源向上找最近的、开了水平滚动的 ScrollViewer。</summary>
    private static ScrollViewer? FindAncestorHorizontalScrollViewer(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is ScrollViewer sv
                && sv.ScrollableWidth > 0
                && sv.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled)
                return sv;
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        }
        return null;
    }

    private void AnnualReportCard_Click(object sender, MouseButtonEventArgs e)
    {
        ((GameViewModel)DataContext).OpenAnnualReportCommand.Execute(null);
    }

    /// <summary>
    /// 服务器卡片右键菜单打开时**在代码里挂载命令**。
    /// <para>
    /// ContextMenu 位于独立的 Popup 视觉树，XAML 绑定方案实测都绑不到命令：
    /// <c>RelativeSource AncestorType=ItemsControl</c> 找不到祖先（不在同一视觉树）；
    /// 把 <c>ContextMenu.DataContext</c> 设成 <c>PlacementTarget.DataContext</c> 则 DataContext
    /// 变成 <c>ServerEntry</c>，而命令挂在 <c>GameViewModel</c> 上，同样取不到。
    /// 于是在 <c>Opened</c> 时按 <c>PlacementTarget</c> 拿到当前条目，
    /// 再用 <c>DataContext</c>（GameViewModel）里的命令显式赋值 —— 百分百可靠。
    /// </para>
    /// </summary>
    private ServerEntry? _pendingServer;

    private void ServerMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;

        var vm = DataContext as GameViewModel;
        _pendingServer = (menu.PlacementTarget as FrameworkElement)?.DataContext as ServerEntry;
        if (vm is null || _pendingServer is null) return;

        // 诊断结论：vm / entry / Tag / Items 全部正常（Cmd=null 只是诊断跑在挂载之前，
        // 属预期），CanExecute 也为 true，但点击仍无反应 —— 说明
        // **ContextMenu 关闭后靠 CommandParameter 传参这条路不可靠**
        // （Popup 关闭会重建 / 回收 MenuItem 容器）。
        // 改为：命令只决定「是否可点」，真正执行走菜单项自己的 Click 事件。
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.IsEnabled = true;
            if (Equals(item.Tag, "delete"))
                item.Command = vm.DeleteServerCommand;
        }
    }

    /// <summary>菜单「编辑」：直接执行，不依赖 Command 往返。</summary>
    private void ServerMenu_EditClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is GameViewModel vm && _pendingServer is not null)
            vm.EditServerCommand.Execute(_pendingServer);
    }

    /// <summary>菜单「删除」：直接执行，不依赖 Command 往返。</summary>
    private void ServerMenu_DeleteClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is GameViewModel vm && _pendingServer is not null)
            vm.DeleteServerCommand.Execute(_pendingServer);
    }
}
