using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows.Input;
using Chert.Core.Profiles;
using Chert.Core.Recommend;
using Chert.Core.Statistics;
using Chert.Core.Mvvm;
using Chert.App.Services;

namespace Chert.App.ViewModels;

/// <summary>主页视图模型：快速启动 + 游玩统计 + 推荐预览。</summary>
public class HomeViewModel : ObservableObject
{
    private ObservableCollection<VersionEntry> _versions = new();
    private VersionEntry? _selectedVersion;
    private string _statusMessage = "";
    private PlayStats _stats = new();
    private ObservableCollection<RecommendationItem> _recommendations = new();

    public ObservableCollection<VersionEntry> Versions
    {
        get => _versions;
        set => SetField(ref _versions, value);
    }

    public VersionEntry? SelectedVersion
    {
        get => _selectedVersion;
        set => SetField(ref _selectedVersion, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public PlayStats Stats
    {
        get => _stats;
        set => SetField(ref _stats, value);
    }

    public ObservableCollection<RecommendationItem> Recommendations
    {
        get => _recommendations;
        set => SetField(ref _recommendations, value);
    }

    public ICommand LaunchCommand { get; }
    public ICommand RefreshCommand { get; }

    public HomeViewModel()
    {
        LaunchCommand = new AsyncRelayCommand(_ => LaunchAsync(), _ => SelectedVersion is not null);
        RefreshCommand = new RelayCommand(_ => Refresh());
        Refresh();
        _ = LoadStatsAndRecommendAsync();
    }

    public void Refresh()
    {
        var gameRoot = LauncherService.Instance.GameRoot;
        Versions = new ObservableCollection<VersionEntry>(
            LauncherService.Instance.ListInstalledVersions()
                .Select(t => new VersionEntry { Id = t.Id, Type = t.Type }));
        // 同步加载统计，首页快速启动默认选中上次启动的版本（RecentVersion）
        Stats = PlaytimeTracker.Load(gameRoot);
        var recent = Stats.RecentVersion;
        SelectedVersion = Versions.FirstOrDefault(v => v.Id == recent) ?? Versions.FirstOrDefault();
        StatusBarViewModel.Current.RefreshAsync();
    }

    private async Task LaunchAsync()
    {
        if (SelectedVersion is null) return;
        await LaunchCoordinator.LaunchAsync(SelectedVersion.Id, s => StatusMessage = s);
    }

    private async Task LoadStatsAndRecommendAsync()
    {
        try
        {
            var gameRoot = LauncherService.Instance.GameRoot;
            // Stats 已在 Refresh() 同步加载（用于首页默认选中上次启动版本），这里只加载推荐
            var profile = ProfileStore.Load(gameRoot);
            var list = await RecommendationEngine.BuildAsync(gameRoot, profile, new HttpClient(), null);
            Recommendations = new ObservableCollection<RecommendationItem>(list.Take(4).ToList());
        }
        catch
        {
            /* 推荐加载失败时忽略，页面其余部分仍可用 */
        }
    }
}
