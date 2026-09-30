using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Chert.App.Themes;
using Chert.Core.Localization;
using Chert.Core.Mvvm;

namespace Chert.App.ViewModels;

/// <summary>节日中心某一项内容推荐（把类型枚举换成可显示文案）。</summary>
public sealed class SeasonalPickCard
{
    public string Title { get; init; } = "";
    public string Note { get; init; } = "";
    public string KindText { get; init; } = "";
    public string? Url { get; init; }
}

/// <summary>
/// 清单 #31 / #42 / #43 / #44：节日中心。
/// 汇总当前节日的限时活动倒计时、服务器推荐、内容推荐与公告，
/// 全部来自远程 <c>config.json</c>，无需发新版即可更新。
/// </summary>
public class SeasonalHubViewModel : ObservableObject
{
    private string _seasonText = "";
    private bool _hasSeason;
    private string _statusMessage = "";
    private ObservableCollection<SeasonalEventCard> _events = new();
    private ObservableCollection<SeasonalServerEntry> _servers = new();
    private ObservableCollection<SeasonalPickCard> _picks = new();

    public string SeasonText
    {
        get => _seasonText;
        set => SetField(ref _seasonText, value);
    }

    public bool HasSeason
    {
        get => _hasSeason;
        set => SetField(ref _hasSeason, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public ObservableCollection<SeasonalEventCard> Events
    {
        get => _events;
        set => SetField(ref _events, value);
    }

    public ObservableCollection<SeasonalServerEntry> Servers
    {
        get => _servers;
        set => SetField(ref _servers, value);
    }

    public ObservableCollection<SeasonalPickCard> Picks
    {
        get => _picks;
        set => SetField(ref _picks, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand CopyAddressCommand { get; }
    public ICommand OpenUrlCommand { get; }

    public SeasonalHubViewModel()
    {
        RefreshCommand = new RelayCommand(_ => Refresh());
        CopyAddressCommand = new RelayCommand(p => CopyAddress(p as string));
        OpenUrlCommand = new RelayCommand(p => OpenUrl(p as string));
        Refresh();
    }

    public void Refresh()
    {
        var config = SeasonalThemeManager.CurrentConfig;
        var key = SeasonalThemeManager.CurrentSeasonKey;

        HasSeason = !string.IsNullOrWhiteSpace(key);

        // 词条缺失时 LocaleManager.T 会原样返回 key，此处退化为直接显示 key，避免露出未翻译标记
        var nameKey = $"seasonal.{key}.name";
        var name = HasSeason ? LocaleManager.T(nameKey) : "";
        if (HasSeason && (string.IsNullOrWhiteSpace(name) || string.Equals(name, nameKey, StringComparison.Ordinal)))
            name = key!;

        SeasonText = HasSeason
            ? $"{LocaleManager.T("seasonal.current")}：{name}"
            : LocaleManager.T("seasonal.none");

        if (config is null)
        {
            Events = new ObservableCollection<SeasonalEventCard>();
            Servers = new ObservableCollection<SeasonalServerEntry>();
            Picks = new ObservableCollection<SeasonalPickCard>();
            StatusMessage = LocaleManager.T("seasonal.hub_loading");
            return;
        }

        var now = System.DateTimeOffset.Now;
        Events = new ObservableCollection<SeasonalEventCard>(
            SeasonalContentService.BuildEventCards(config, key ?? "", now));
        Servers = new ObservableCollection<SeasonalServerEntry>(
            SeasonalContentService.BuildServers(config, key ?? ""));
        Picks = new ObservableCollection<SeasonalPickCard>(
            SeasonalContentService.BuildPicks(config, key ?? "")
                .Select(p => new SeasonalPickCard
                {
                    Title = p.Title,
                    Note = p.Note ?? "",
                    KindText = KindText(p.KindEnum),
                    Url = p.ResolvedUrl
                }));

        var total = Events.Count + Servers.Count + Picks.Count;
        StatusMessage = total == 0
            ? LocaleManager.T("seasonal.hub_empty")
            : string.Format(LocaleManager.T("seasonal.hub_summary"), Events.Count, Servers.Count, Picks.Count);
    }

    private static string KindText(SeasonalPickKind kind) => kind switch
    {
        SeasonalPickKind.Mod => LocaleManager.T("seasonal.kind.mod"),
        SeasonalPickKind.Modpack => LocaleManager.T("seasonal.kind.modpack"),
        SeasonalPickKind.ResourcePack => LocaleManager.T("seasonal.kind.resourcepack"),
        SeasonalPickKind.Shader => LocaleManager.T("seasonal.kind.shader"),
        SeasonalPickKind.Version => LocaleManager.T("seasonal.kind.version"),
        _ => LocaleManager.T("seasonal.kind.other")
    };

    private void CopyAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return;
        try
        {
            Clipboard.SetText(address);
            StatusMessage = LocaleManager.T("seasonal.copied");
        }
        catch
        {
            StatusMessage = LocaleManager.T("seasonal.copy_failed");
        }
    }

    private static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // 打不开就算了
        }
    }
}
