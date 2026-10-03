using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using Chert.App.ViewModels;

namespace Chert.App.Views;

public partial class SettingsView : UserControl
{
    private SettingsViewModel Vm => (SettingsViewModel)DataContext;

    public SettingsView()
    {
        InitializeComponent();
        CategoryList.SelectedIndex = 0;
        ShowCategory("General");
    }

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryList.SelectedItem is ListBoxItem item)
            ShowCategory(item.Tag as string ?? "General");
    }

    private FrameworkElement? _currentPanel;

    /// <summary>按分类 tag 切换可见的设置面板（General/Launch/...），并做从右淡入 / 向左淡出切换。</summary>
    public void ShowCategory(string tag)
    {
        FrameworkElement? target = tag switch
        {
            "General" => GridGeneral,
            "Launch" => GridLaunch,
            "Download" => GridDownload,
            "Recommend" => GridRecommend,
            "Accounts" => GridAccounts,
            "Ai" => AiSettingsHost,
            "Music" => GridMusic,
            "Appearance" => GridAppearance,
            "About" => GridAbout,
            _ => null
        };
        if (target is null) return;

        // 同分类不重复动画
        if (ReferenceEquals(_currentPanel, target)) return;

        // 首屏：直接显示并淡入
        if (_currentPanel is null)
        {
            CollapseAllExcept(target);
            target.Visibility = Visibility.Visible;
            MotionFX.SlideInFromRight(target);
            _currentPanel = target;
            return;
        }

        // 切换：旧面板向左滑出淡出，新面板从右滑入淡入（二者在 Grid 同格重叠）
        var old = _currentPanel;
        CollapseAllExcept(target, old);
        target.Visibility = Visibility.Visible;
        MotionFX.SlideOutToLeft(old, () =>
        {
            // 退场结束：仅当没有再次切回它时才折叠，避免快速来回切换被误收
            if (!ReferenceEquals(_currentPanel, old))
                old.Visibility = Visibility.Collapsed;
        });
        MotionFX.SlideInFromRight(target);
        _currentPanel = target;
    }

    /// <summary>折叠所有分类面板，仅保留 keep（及可选的 alsoKeep）。</summary>
    private void CollapseAllExcept(FrameworkElement keep, FrameworkElement? alsoKeep = null)
    {
        foreach (var el in new FrameworkElement?[]
                 {
                     GridGeneral, GridLaunch, GridDownload, GridRecommend,
                     GridAccounts, AiSettingsHost, GridMusic, GridAppearance, GridAbout
                 })
        {
            if (el is not null && !ReferenceEquals(el, keep) && !ReferenceEquals(el, alsoKeep))
                el.Visibility = Visibility.Collapsed;
        }
    }


    /// <summary>由 MainWindow 全局侧边栏路由调用（id 对应 <see cref="SidebarModel.Settings"/>）。</summary>
    public void ShowSidebarItem(string id) => ShowCategory(id switch
    {
        "general" => "General",
        "launch" => "Launch",
        "download" => "Download",
        "recommend" => "Recommend",
        "account" => "Accounts",
        "music" => "Music",
        "ai" => "Ai",
        "appearance" => "Appearance",
        "about" => "About",
        _ => "General"
    });

    private async void AddAuthlib_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            await vm.AddAuthlibAccount(vm.AuthlibServerUrl, vm.AuthlibEmail, AuthlibPw.Password);
    }

    /// <summary>
    /// 双击账号条目 = 设为当前账号（problem3 多角色）。
    /// 多角色时切账号最高频的动作，给个双击直达；单项仍可用下方「设为当前」按钮。
    /// </summary>
    private void AccountList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && vm.SelectedAccount is not null)
            vm.SetActiveAccountCommand.Execute(null);
    }

    private void OpenUrl_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is System.Windows.Documents.Hyperlink link && link.NavigateUri is not null)
                Process.Start(new ProcessStartInfo(link.NavigateUri.ToString()) { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }
}
