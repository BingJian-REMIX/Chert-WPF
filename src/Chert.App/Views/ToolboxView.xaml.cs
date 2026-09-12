using System.Linq;
using System.Windows.Controls;
using Chert.App.ViewModels;

namespace Chert.App.Views;

public partial class ToolboxView : UserControl
{
    private ToolboxViewModel Vm => (ToolboxViewModel)DataContext;

    public ToolboxView()
    {
        InitializeComponent();
    }

    /// <summary>由 MainWindow 全局侧边栏路由调用，切换到指定面板（规格 1.4）。</summary>
    public void ShowPanel(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;

        // 开发工具在全局侧边栏拆成 4 项（moddev/packmaker/nbt/command），统一归入「开发工具」面板
        var lookup = id switch
        {
            "moddev" or "packmaker" or "command" => "dev",
            _ => id
        };

        var panel = Vm.PanelItems.FirstOrDefault(p => p.Id == lookup);
        if (panel is not null)
        {
            Vm.SelectedPanel = panel;
            // bug #13：开发工具面板按入口切换内部模式（命令速查 / Mod 骨架 / 资源包创建）
            if (lookup == "dev" && panel.View is DevToolsView dev)
                dev.SetMode(id);
            return;
        }

        // 全局侧边栏有、但工具箱内部列表未收录的面板（崩溃分析）
        var extra = lookup switch
        {
            "crash" => (UserControl)new CrashAnalysisView(),
            _ => null
        };
        if (extra is not null) Vm.SelectedView = extra;
    }

    /// <summary>由全局搜索调用：标题栏全局搜索已通过 <see cref="ShowPanel"/> 跳到匹配面板，
    /// 工具箱页内不再叠加搜索框，故此处仅保留兼容签名、无实际操作。</summary>
    public void SetSearchKeyword(string keyword)
    {
        // 工具箱页内无独立搜索框（命令速查/命令助手等子面板自带搜索），全局搜索的「预填」交给子面板自身，
        // 此处无需把关键词写入页级搜索。保留方法签名以避免 MainWindow 调用点改动。
    }

    /// <summary>bug #17：由全局搜索调用——返回与关键词匹配的工具箱面板 Id，无匹配返回 null。</summary>
    public string? MatchPanelId(string keyword) => Vm.MatchPanelId(keyword);
}
