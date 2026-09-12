using System.Windows.Controls;
using Chert.App.ViewModels;

namespace Chert.App.Views;

public partial class DevToolsView : UserControl
{
    public DevToolsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// bug #13：全局侧边栏把「Mod 开发 / 资源包创建 / 命令速查」拆成三个入口，但都路由到本面板。
    /// 此前只切面板不切模式，点「资源包创建」看到的仍是命令速查表（表现为"链接错误"）。
    /// </summary>
    public void SetMode(string? id)
    {
        if (DataContext is not DevToolsViewModel vm) return;
        vm.Mode = id switch
        {
            "moddev" or "mod" => "mod",
            "packmaker" or "resourcepack" => "resourcepack",
            _ => "commands"
        };
    }
}
