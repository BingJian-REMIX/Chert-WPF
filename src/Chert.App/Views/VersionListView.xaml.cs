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

    private void BackButton_Click(object sender, System.Windows.RoutedEventArgs e) =>
        (OnBack ?? BigPageNavigator.Close)();

    private void OnSettingsRequested(VersionEntry entry)
    {
        // 由版本列表进入版本设置大页；返回时重新打开版本列表
        BigPageNavigator.Show(new VersionSettingsView(
            LauncherService.Instance.GameRoot, entry.Id, entry.Type, onBack: () => BigPageNavigator.Show(new VersionListView())));
    }
}
