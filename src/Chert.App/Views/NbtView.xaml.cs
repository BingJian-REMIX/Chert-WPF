using System.Windows;
using System.Windows.Controls;
using Chert.App.ViewModels;

namespace Chert.App.Views;

public partial class NbtView : UserControl
{
    public NbtView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// TreeView.SelectedItem 是只读依赖属性，没法直接双向绑定到 VM，
    /// 所以在这里把选中项转发给 ViewModel。
    /// </summary>
    private void NbtTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is NbtViewModel vm && e.NewValue is NbtNodeViewModel node)
            vm.SelectedNode = node;
    }

    /// <summary>快捷入口下拉：选中即打开对应的 level.dat。</summary>
    private void QuickFile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not NbtViewModel vm) return;
        if (sender is not ComboBox { SelectedItem: string path }) return;

        vm.OpenQuickCommand.Execute(path);
    }
}
