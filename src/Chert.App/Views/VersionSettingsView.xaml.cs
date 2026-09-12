using System.Windows;
using System.Windows.Controls;
using Chert.App.Services;
using Chert.App.ViewModels;

namespace Chert.App.Views;

/// <summary>
/// 版本设置面板（对齐 MCLCS-Linux 的 VersionSettingsDialog）。
/// 宿主窗口负责创建：见 <see cref="VersionSettingsWindow"/>。
/// </summary>
public partial class VersionSettingsView : UserControl
{
    public VersionSettingsViewModel VM { get; }

    /// <summary>返回上一页（大页导航关闭，或重新打开版本列表）；未设置时回退到关闭宿主窗口。</summary>
    public Action? OnBack { get; set; }

    public VersionSettingsView(string gameRoot, string versionId, string versionType, Action? onBack = null)
    {
        OnBack = onBack;
        VM = new VersionSettingsViewModel(gameRoot, versionId, versionType);
        DataContext = VM;
        InitializeComponent();
        VM.RequestClose += CloseSelf;
        Loaded += (_, _) =>
        {
            // X/关闭按钮统一走 CloseSelf：与返回键、删除后关闭共用同一套安全降级逻辑，
            // 绝不在大页形态下直接 Window.GetWindow(this)?.Close() 误关 MainWindow。
            CloseButton.Click += (_, _) => CloseSelf();
        };
    }

    private void BackButton_Click(object sender, System.Windows.RoutedEventArgs e) => CloseSelf();

    /// <summary>
    /// 安全关闭本视图。<br/>
    /// 本视图有两种宿主形态：①独立模态窗口 <see cref="VersionSettingsWindow"/>；②主窗口内的「大页导航」。
    /// 形态②下 <c>Window.GetWindow(this)</c> 返回的是 <b>MainWindow</b>，直接 <c>Close()</c> 会把整个启动器关掉——
    /// 这正是「删除版本后启动器突然消失、看起来像崩溃、但 crash log 里查不到任何异常」的真实根因。
    /// 因此这里按优先级降级处理：先走返回导航，再判断宿主是否确实是版本设置窗口，最后才回退到关闭大页。
    /// </summary>
    private void CloseSelf()
    {
        if (OnBack is not null) { OnBack(); return; }

        // 仅当宿主确实是版本设置自己的窗口时才允许关窗；绝不关闭 MainWindow。
        if (Window.GetWindow(this) is VersionSettingsWindow own) { own.Close(); return; }

        BigPageNavigator.Close();
    }
}
