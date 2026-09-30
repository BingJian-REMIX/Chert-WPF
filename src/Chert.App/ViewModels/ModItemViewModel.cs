using System.Windows.Media;
using Chert.Core.Mods;
using Chert.Core.Mvvm;

namespace Chert.App.ViewModels;

/// <summary>
/// Mod 列表项（清单 #66）：在 <see cref="ModEntry"/> 之上附加勾选状态与核验结果，
/// 让批量处理（批量核验 / 批量更新 / 批量启用禁用 / 批量卸载）有稳定的绑定目标。
/// </summary>
public class ModItemViewModel : ObservableObject
{
    private ModEntry _mod;
    private bool _isSelected;
    private string _verifyText = "";
    private string _verifyLevel = "none";
    private string _verifySha1 = "";

    public ModItemViewModel(ModEntry mod) => _mod = mod;

    /// <summary>底层数据模型。</summary>
    public ModEntry Mod => _mod;

    /// <summary>是否被勾选（批量处理的目标集合）。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    /// <summary>用新的扫描结果刷新底层模型，并通知所有派生属性。</summary>
    public void Replace(ModEntry mod)
    {
        _mod = mod;
        OnPropertyChanged(nameof(Mod));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ModId));
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(InstalledVersion));
        OnPropertyChanged(nameof(LatestVersion));
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(Loader));
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(UpdateText));
    }

    public string Name => _mod.Name;
    public string ModId => string.IsNullOrWhiteSpace(_mod.ModId) ? "-" : _mod.ModId;
    public string FileName => _mod.FileName;
    public string InstalledVersion => _mod.InstalledVersion;
    public string LatestVersion => _mod.LatestVersion ?? "";
    public bool HasUpdate => _mod.HasUpdate;
    public string Loader => string.IsNullOrWhiteSpace(_mod.Loader) ? "-" : _mod.Loader;
    public bool Enabled => _mod.Enabled;
    public string StateText => Enabled ? "已启用" : "已禁用";
    public string UpdateText => HasUpdate ? "有更新" : "最新";

    /// <summary>核验结论文本；未核验时为空。</summary>
    public string VerifyText
    {
        get => _verifyText;
        set
        {
            if (SetField(ref _verifyText, value)) OnPropertyChanged(nameof(VerifyBrush));
        }
    }

    /// <summary>核验等级：none / ok / warn / error。</summary>
    public string VerifyLevel
    {
        get => _verifyLevel;
        set
        {
            if (SetField(ref _verifyLevel, value)) OnPropertyChanged(nameof(VerifyBrush));
        }
    }

    /// <summary>核验得到的 SHA-1（仅成功时非空）。</summary>
    public string VerifySha1
    {
        get => _verifySha1;
        set => SetField(ref _verifySha1, value);
    }

    /// <summary>核验结论的显示颜色。</summary>
    public Brush VerifyBrush => _verifyLevel switch
    {
        "ok" => OkBrush,
        "warn" => WarnBrush,
        "error" => ErrorBrush,
        _ => DimBrush
    };

    /// <summary>套用一次核验结果。</summary>
    public void ApplyVerify(ModVerifyResult result)
    {
        VerifyText = result.Message;
        VerifyLevel = result.Level;
        VerifySha1 = result.Sha1;
    }

    private static readonly SolidColorBrush OkBrush = new(Color.FromRgb(0x2E, 0xCC, 0x71));
    private static readonly SolidColorBrush WarnBrush = new(Color.FromRgb(0xF3, 0x9C, 0x12));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xE7, 0x4C, 0x3C));
    private static readonly SolidColorBrush DimBrush = new(Color.FromRgb(0x88, 0x88, 0x88));
}
