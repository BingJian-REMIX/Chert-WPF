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
    private void ServerMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;

        var vm = DataContext as GameViewModel;
        var entry = (menu.PlacementTarget as FrameworkElement)?.DataContext as ServerEntry;
        if (vm is null || entry is null) return;

        // ★ 菜单项的 x:Name 定义在 DataTemplate 内 —— 那属于**局部名字作用域**（每张卡片一份），
        //   code-behind 里直接写 ServerEditItem 编译不过（CS0103）。
        //   改为遍历 ContextMenu 的逻辑树找 MenuItem（它们是菜单的直接逻辑子节点）。
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (ReferenceEquals(item.Tag, "edit"))
            {
                item.Command = vm.EditServerCommand;
                item.CommandParameter = entry;
            }
            else if (ReferenceEquals(item.Tag, "delete"))
            {
                item.Command = vm.DeleteServerCommand;
                item.CommandParameter = entry;
            }
        }
    }
}
