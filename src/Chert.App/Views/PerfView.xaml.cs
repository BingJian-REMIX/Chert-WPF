using System.Windows.Controls;
using Chert.App.ViewModels;

namespace Chert.App.Views;

/// <summary>
/// 性能/实例监控页。PerfViewModel 内部持有 DispatcherTimer，页面卸载时必须 Dispose 停止，
/// 否则反复打开性能页会让多个 timer 泄漏、后台持续 Sample 触发异常。
/// </summary>
public partial class PerfView : UserControl
{
    public PerfView()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
        {
            if (DataContext is PerfViewModel vm) vm.Dispose();
        };
    }
}
