using System.Windows.Controls;

namespace Chert.App.Views;

public partial class InstallView : UserControl
{
    public InstallView()
    {
        InitializeComponent();
        // 版本清单延迟到页面显示时才拉（构造期拉会拖慢 MainWindow 启动，且无网络时白等）
        Loaded += async (_, _) =>
        {
            if (DataContext is Chert.App.ViewModels.InstallViewModel vm)
                await vm.EnsureVersionsLoadedAsync();
        };
    }
}
