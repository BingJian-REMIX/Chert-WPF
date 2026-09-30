using System.Windows.Controls;

namespace Chert.App.Views;

/// <summary>清单 #35 ~ #40：局域网联动页面（对等模式，不做远端部署）。</summary>
public partial class LanLinkView : UserControl
{
    public LanLinkView() => InitializeComponent();

    /// <summary>供外部（如全局搜索命中后）主动搜索一次。</summary>
    public void Refresh() => _ = (DataContext as ViewModels.LanLinkViewModel)?.RefreshCommand.Execute(null);
}
