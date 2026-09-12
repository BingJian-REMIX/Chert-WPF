using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows.Input;
using Chert.Core.Launcher;
using Chert.Core.Models;
using Chert.Core.Mods;
using Chert.Core.Mvvm;
using Chert.Core.Profiles;
using Chert.Core.Recommend;
using Chert.Core.Utils;
using Chert.App.Services;

namespace Chert.App.ViewModels;

/// <summary>智能推荐页 ViewModel：卡片流展示推荐（依赖补全 / 热门榜单 / 更新 / 场景）。</summary>
public class RecommendationViewModel : ObservableObject
{
    private ObservableCollection<RecommendationItem> _items = new();
    private string _statusMessage = "";
    private bool _isBusy;

    public ObservableCollection<RecommendationItem> Items
    {
        get => _items;
        set => SetField(ref _items, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand NotInterestedCommand { get; }
    public ICommand DetailsCommand { get; }

    public RecommendationViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(_ => LoadAsync(), _ => !IsBusy);
        InstallCommand = new AsyncRelayCommand(_ => InstallAsync((RecommendationItem)_!), _ => !IsBusy);
        NotInterestedCommand = new RelayCommand(_ => NotInterested(_ as RecommendationItem));
        DetailsCommand = new RelayCommand(_ => Details(_ as RecommendationItem));
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var profile = ProfileStore.Load(LauncherService.Instance.GameRoot);
            var list = await RecommendationEngine.BuildAsync(LauncherService.Instance.GameRoot, profile,
                new HttpClient(), null);
            Items = new ObservableCollection<RecommendationItem>(list);
            StatusMessage = Items.Count > 0
                ? $"为你推荐 {Items.Count} 个内容（依赖补全类已用醒目标记）"
                : "暂无推荐，安装更多 Mod 后会有同类推荐";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载推荐失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InstallAsync(RecommendationItem item)
    {
        if (item is null || string.IsNullOrEmpty(item.ProjectId)) return;
        IsBusy = true;
        try
        {
            var profile = ProfileStore.Load(LauncherService.Instance.GameRoot);
            var loader = DetectLoader();
            var gameVersion = RuleEngine.ExtractGameVersion(profile.LastVersionId);
            var modsDir = PathEx.ModsDir(LauncherService.Instance.GameRoot);
            StatusMessage = $"正在安装 {item.Title} …";
            var ok = await LauncherService.Instance.DownloadModAsync(item.ProjectId, modsDir, gameVersion, loader);
            StatusMessage = ok ? $"已安装 {item.Title}" : $"安装 {item.Title} 失败";
            if (ok) Items.Remove(item);
        }
        catch (Exception ex)
        {
            StatusMessage = $"安装出错：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotInterested(RecommendationItem? item)
    {
        if (item is null) return;
        Items.Remove(item);
        StatusMessage = $"已对 {item.Title} 标记不感兴趣";
    }

    private void Details(RecommendationItem? item)
    {
        if (item is null) return;
        var slug = string.IsNullOrEmpty(item.Slug) ? item.ProjectId : item.Slug;
        if (string.IsNullOrEmpty(slug)) return;
        var url = $"https://modrinth.com/mod/{slug}";
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { StatusMessage = $"请手动访问：{url}"; }
    }

    private LoaderType DetectLoader()
    {
        var mods = new ModManager(LauncherService.Instance.GameRoot, new HttpClient(), null!).ListInstalledMods();
        var loaders = mods.Select(m => m.Loader).ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        if (loaders.Contains("fabric")) return LoaderType.Fabric;
        if (loaders.Contains("forge")) return LoaderType.Forge;
        if (loaders.Contains("neoforge")) return LoaderType.NeoForge;
        if (loaders.Contains("quilt")) return LoaderType.Quilt;
        return LoaderType.Any;
    }
}
