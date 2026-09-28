using System.Windows.Controls;
using Chert.App.Services;
using Chert.App.ViewModels;

namespace Chert.App.Views;

public partial class VersionListView : UserControl
{
    /// <summary>返回上一页（由打开方设置：大页导航关闭，或重新打开版本列表）。</summary>
    public Action? OnBack { get; set; }

    public VersionListView()
    {
        InitializeComponent();
        if (DataContext is VersionListViewModel vm)
            vm.SettingsRequested += OnSettingsRequested;
    }


    /// <summary>双击列表项直接进入该版本的版本设置（清单 2.6 功能改进）。</summary>
    private void VersionsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not ListBox list) return;
        if (list.SelectedItem is not VersionEntry entry) return;

        // 与工具栏「版本设置」按钮走同一路径，由 VM 触发大页导航
        if (DataContext is VersionListViewModel vm && vm.OpenSettingsCommand.CanExecute(entry))
            vm.OpenSettingsCommand.Execute(entry);
        e.Handled = true;
    }
    private void BackButton_Click(object sender, System.Windows.RoutedEventArgs e) =>
        (OnBack ?? BigPageNavigator.Close)();

    private void OnSettingsRequested(VersionEntry entry)
    {
        // 由版本列表进入版本设置大页；返回时重新打开版本列表
        BigPageNavigator.Show(new VersionSettingsView(
            LauncherService.Instance.GameRoot, entry.Id, entry.Type, onBack: () => BigPageNavigator.Show(new VersionListView())));
    }
}
