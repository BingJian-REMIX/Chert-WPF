using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Chert.Core.Mvvm;
using Chert.Core.Save;
using Chert.App.Services;

namespace Chert.App.ViewModels;

/// <summary>
/// §二.4 启动前兼容性提示的视图模型。
/// 列出"版本高于目标游戏版本"的存档，并提供三选项：降级 / 安装版本 / 忽略。
/// </summary>
public class SaveCompatPromptViewModel : ObservableObject
{
    private readonly string _gameRoot;
    private readonly string _versionId;
    private string _statusMessage = "";
    private bool _isBusy;

    public ObservableCollection<SaveRow> Incompatible { get; } = new();

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

    /// <summary>用户决策：true=继续启动（已降级或忽略），false=取消。</summary>
    public bool Proceed { get; private set; }

    public ICommand DowngradeAllCommand { get; }
    public ICommand IgnoreCommand { get; }
    public ICommand InstallVersionCommand { get; }

    /// <summary>决策完成（传 true 继续 / false 取消）。宿主据此关闭窗口。</summary>
    public event Action<bool>? Decision;

    public SaveCompatPromptViewModel(string gameRoot, string versionId,
        IEnumerable<SaveCompatibilityReport> incompatible)
    {
        _gameRoot = gameRoot;
        _versionId = versionId;

        foreach (var r in incompatible)
        {
            Incompatible.Add(new SaveRow
            {
                SaveName = r.SaveName,
                SavePath = r.SavePath,
                CurrentVersion = r.SaveGameVersion ?? $"dv={r.SaveDataVersion}",
                DataVersion = r.SaveDataVersion,
                CompatText = r.Message,
                SeverityColor = r.Severity == SaveCompatibilitySeverity.MuchNewer ? "#E0533A" : "#E0A040"
            });
        }

        DowngradeAllCommand = new AsyncRelayCommand(_ => DowngradeAllAsync(), _ => !IsBusy);
        IgnoreCommand = new RelayCommand(_ => { Proceed = true; Decision?.Invoke(true); });
        InstallVersionCommand = new RelayCommand(_ =>
        {
            Proceed = false;
            StatusMessage = $"请在「安装新版本」中安装对应版本后再启动（如 {incompatible.FirstOrDefault()?.SaveGameVersion}）。";
            Decision?.Invoke(false);
        });

        StatusMessage = Incompatible.Count > 0
            ? $"有 {Incompatible.Count} 个存档的版本高于目标游戏版本 {versionId}，可能无法打开。"
            : "";
    }

    private async Task DowngradeAllAsync()
    {
        IsBusy = true;
        try
        {
            var savesDir = SaveCompatibilityDetector.SavesDir(_gameRoot);
            var ok = 0;
            foreach (var row in Incompatible)
            {
                var savePath = Path.Combine(savesDir, row.SaveName);
                var plan = await SaveDowngrader.DowngradeAsync(savePath, _versionId, DowngradeMethod.QuickModifyDataVersion);
                if (plan.Success) ok++;
            }
            StatusMessage = $"已降级 {ok}/{Incompatible.Count} 个存档到 {_versionId}（均保留备份）。";
            Proceed = true;
            Decision?.Invoke(true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"降级异常：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
