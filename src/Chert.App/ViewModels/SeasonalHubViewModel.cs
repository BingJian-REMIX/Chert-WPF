using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Chert.App.Themes;
using Chert.App.Views;
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

    private bool _hasEvents;
    private bool _hasServers;
    private bool _hasPicks;

    /// <summary>限时活动是否有内容（空态提示用）。</summary>
    public bool HasEvents
    {
        get => _hasEvents;
        private set => SetField(ref _hasEvents, value);
    }

    /// <summary>节日服务器推荐是否有内容。</summary>
    public bool HasServers
    {
        get => _hasServers;
        private set => SetField(ref _hasServers, value);
    }

    /// <summary>节日内容推荐是否有内容。</summary>
    public bool HasPicks
    {
        get => _hasPicks;
        private set => SetField(ref _hasPicks, value);
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

    /// <summary>清单 #34：彩蛋小游戏入口（愚人节期间在节日中心露出）。</summary>
    public ICommand PlayEasterEggCommand { get; }

    /// <summary>当前节日为愚人节时显示彩蛋入口。</summary>
    public bool EasterEggVisible { get; private set; }

    public SeasonalHubViewModel()
    {
        RefreshCommand = new RelayCommand(_ => Refresh());
        CopyAddressCommand = new RelayCommand(p => CopyAddress(p as string));
        OpenUrlCommand = new RelayCommand(p => OpenUrl(p as string));
        PlayEasterEggCommand = new RelayCommand(_ => PlayEasterEgg());
        Refresh();
    }

    /// <summary>节日 key → 本地化词条 id。归一化：只保留字母数字并转小写（mid_autumn → midautumn）。</summary>
    private static string SeasonNameKey(string key)
    {
        var slug = new string(key.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return $"seasonal.{slug}.name";
    }

    /// <summary>词条确实缺失时的兜底：把 mid_autumn 这类 key 收拾成人能读的样子（而非原样露出下划线）。</summary>
    private static string Prettify(string key)
    {
        var parts = key.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0
            ? key
            : string.Join(" ", parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    public void Refresh()
    {
        var config = SeasonalThemeManager.CurrentConfig;
        var key = SeasonalThemeManager.CurrentSeasonKey;

        // 清单 #34：愚人节期间露出彩蛋入口
        EasterEggVisible = !string.IsNullOrWhiteSpace(key) &&
                           (key.Contains("april", System.StringComparison.OrdinalIgnoreCase) ||
                            key.Contains("fool", System.StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(EasterEggVisible));

        HasSeason = !string.IsNullOrWhiteSpace(key);

        // ★ 节日 key 与词条 id 的规范不一致：key 是 mid_autumn（带下划线），
        //   词条是 seasonal.midautumn.name（无下划线）—— 直接拼 key 会查不到，
        //   于是「当前节日」直接露出原始 key（用户反馈的 mid_autumn）。
        //   这里先把 key 归一化（只保留字母数字、转小写）再查词条。
        var name = HasSeason ? LocaleManager.T(SeasonNameKey(key!)) : "";
        if (HasSeason && (string.IsNullOrWhiteSpace(name) || name == SeasonNameKey(key!)))
            name = Prettify(key!);

        SeasonText = HasSeason
            ? $"{LocaleManager.T("seasonal.current")}：{name}"
            : LocaleManager.T("seasonal.none");

        if (config is null)
        {
            Events = new ObservableCollection<SeasonalEventCard>();
            Servers = new ObservableCollection<SeasonalServerEntry>();
            Picks = new ObservableCollection<SeasonalPickCard>();
            HasEvents = HasServers = HasPicks = false;
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

        // 分节空态：三栏各自给一句说明，而不是留白（用户反馈「三栏全空」看不出是没内容还是没加载）
        HasEvents = Events.Count > 0;
        HasServers = Servers.Count > 0;
        HasPicks = Picks.Count > 0;

        var total = Events.Count + Servers.Count + Picks.Count;
        StatusMessage = total == 0
            ? LocaleManager.T("seasonal.hub_empty")
            : string.Format(LocaleManager.T("seasonal.hub_summary"), Events.Count, Servers.Count, Picks.Count);
    }

    private static void PlayEasterEgg()
    {
        try
        {
            var win = new EasterEggGameWindow
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };
            win.Show();
        }
        catch { /* 彩蛋非关键 */ }
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
