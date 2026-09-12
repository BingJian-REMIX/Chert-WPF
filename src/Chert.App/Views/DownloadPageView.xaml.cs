using System.Windows.Controls;
using Chert.App.ViewModels;

namespace Chert.App.Views;

public partial class DownloadPageView : UserControl
{
    public DownloadPageViewModel ViewModel { get; }

    public DownloadPageView()
    {
        InitializeComponent();
        ViewModel = new DownloadPageViewModel();
        DataContext = ViewModel;
    }

    /// <summary>由 MainWindow 侧边栏路由调用，切换到指定副标签并加载内容。</summary>
    public void ShowSubTab(string? id) => ViewModel.SetSubTab(id);

    /// <summary>全局搜索：预填搜索关键词并触发搜索。</summary>
    public void SetSearchKeyword(string keyword)
    {
        ViewModel.Query = keyword;
        ViewModel.SearchCommand.Execute(null);
    }
}
