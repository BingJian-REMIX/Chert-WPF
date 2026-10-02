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
