using System.Windows;
using Chert.Core.Launcher;
using Chert.App.Themes;
using Chert.App.ViewModels;

namespace Chert.App.Views;

public partial class CrashReportView : Window
{
    public CrashReportView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 展示一次崩溃的分析报告；当可自动修复时提供"尝试自动修复"按钮。
    /// <paramref name="relaunch"/> 在用户点击修复时调用：执行修复并重新启动游戏，
    /// 返回新的 <see cref="LaunchResult"/>（再次崩溃）或 null（启动成功）。
    /// <paramref name="allowRepair"/> 为 false 时（如策略为"始终拒绝"）不提供修复按钮。
    /// </summary>
    public CrashReportView(LaunchResult result, Func<LaunchResult, Task<LaunchResult?>> relaunch, bool allowRepair = true) : this()
    {
        var vm = new CrashReportViewModel(result, relaunch, allowRepair);
        vm.Repaired += () => Dispatcher.Invoke(Close);
        DataContext = vm;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => AnimationHelper.PlayModalEnter(Card);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
