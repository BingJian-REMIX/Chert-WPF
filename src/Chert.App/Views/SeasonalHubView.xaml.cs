using System.Windows.Controls;

namespace Chert.App.Views;

/// <summary>清单 #31 / #42 / #43 / #44：节日中心页面。</summary>
public partial class SeasonalHubView : UserControl
{
    public SeasonalHubView() => InitializeComponent();

    /// <summary>供外部（如全局搜索命中后）重新拉取节日内容。</summary>
    public void Refresh() => (DataContext as ViewModels.SeasonalHubViewModel)?.Refresh();
}
