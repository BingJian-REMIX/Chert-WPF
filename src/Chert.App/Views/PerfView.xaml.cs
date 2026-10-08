using System.Windows.Controls;
using Chert.App.ViewModels;

namespace Chert.App.Views;

/// <summary>
/// 性能/实例监控页。PerfViewModel 内部持有 DispatcherTimer，离开页面时必须停表，
/// 否则后台会继续每 1.5 秒采样一遍（白烧 CPU，还会反复枚举进程）。
/// <para>
/// 这里<b>不能</b>在 Unloaded 里 Dispose：工具箱的面板视图是缓存的
/// （<c>ToolboxPanelItem.View</c> 只 new 一次，切走再切回还是同一个实例）。
/// 一旦 Dispose，那个 VM 就再也起不来 —— 表现是「性能页第二次打开就不刷新了」，
/// 而且 <c>PerformanceCounter</c> 被释放后系统 CPU/内存两块会直接冻在最后一帧。
/// 所以只 Stop，并把重启交给 Loaded。
/// </para>
/// </summary>
public partial class PerfView : UserControl
{
    public PerfView()
    {
        InitializeComponent();
        Loaded += (_, _) => { if (DataContext is PerfViewModel vm) vm.Start(); };
        Unloaded += (_, _) => { if (DataContext is PerfViewModel vm) vm.Stop(); };
    }
}
