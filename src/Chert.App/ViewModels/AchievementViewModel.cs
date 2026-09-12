using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using Chert.Core.Mvvm;
using Chert.App.Services;

namespace Chert.App.ViewModels;

/// <summary>成就统计行。</summary>
public class AchievementStats : ObservableObject
{
    public string SaveName { get; init; } = "";
    public int Completed { get; set; }
    public int Total { get; set; }
    public int Purple { get; set; } // 紫色稀有成就
    public string Summary => $"达成 {Completed}/{Total}" + (Purple > 0 ? $"（{Purple} 紫色）" : "");
}

/// <summary>
/// 成就展示（规格 2.3 面板 2）：读取存档下 advancements/*.json，
/// 统计达成/未达成/紫色成就数量。
/// </summary>
public class AchievementViewModel : ObservableObject
{
    private ObservableCollection<AchievementStats> _saves = new();
    private string _statusMessage = "";
    private bool _isBusy;

    public ObservableCollection<AchievementStats> Saves
    {
        get => _saves;
        set => SetField(ref _saves, value);
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

    public AchievementViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var gameRoot = LauncherService.Instance.GameRoot;
            var savesDir = Path.Combine(gameRoot, "saves");
            var stats = new List<AchievementStats>();

            if (!Directory.Exists(savesDir))
            {
                StatusMessage = "未找到 saves 目录";
                Saves = new ObservableCollection<AchievementStats>();
                return;
            }

            foreach (var saveDir in Directory.GetDirectories(savesDir))
            {
                var advDir = Path.Combine(saveDir, "advancements");
                if (!Directory.Exists(advDir))
                {
                    stats.Add(new AchievementStats { SaveName = Path.GetFileName(saveDir), Completed = 0, Total = 0 });
                    continue;
                }

                var completed = 0;
                var total = 0;
                var purple = 0;
                var allJson = Directory.GetFiles(advDir, "*.json", SearchOption.AllDirectories);

                foreach (var jsonFile in allJson)
                {
                    try
                    {
                        var text = await File.ReadAllTextAsync(jsonFile);
                        var doc = JsonDocument.Parse(text);
                        var root = doc.RootElement;
                        total++;
                        if (root.TryGetProperty("done", out var done) && done.GetBoolean())
                        {
                            completed++;
                            // 紫色成就判断：display.frame 为 "challenge"
                            if (root.TryGetProperty("display", out var display) &&
                                display.TryGetProperty("frame", out var frame) &&
                                frame.GetString() == "challenge")
                                purple++;
                        }
                    }
                    catch { /* skip malformed */ }
                }

                stats.Add(new AchievementStats
                {
                    SaveName = Path.GetFileName(saveDir),
                    Completed = completed,
                    Total = total,
                    Purple = purple
                });
            }

            Saves = new ObservableCollection<AchievementStats>(stats);
            StatusMessage = $"{stats.Count} 个存档扫描完成";
        }
        catch (Exception ex) { StatusMessage = $"扫描失败：{ex.Message}"; }
        finally { IsBusy = false; }
    }
}
