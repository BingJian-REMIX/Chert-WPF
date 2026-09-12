using System.Windows.Input;
using Chert.Core.Mvvm;

namespace Chert.App.ViewModels;

/// <summary>
/// 工具箱 → Mod 开发环境（对齐 MCLCS-Linux ModDevView）：按用户输入生成最小 Mod 工程骨架
/// （fabric.mod.json / mods.toml + 主类桩 + 构建脚本），省去手工建目录。
/// </summary>
public class ModDevViewModel : ObservableObject
{
    private string _modId = "examplemod";
    public string ModId { get => _modId; set => SetField(ref _modId, value); }

    private string _modName = "Example Mod";
    public string ModName { get => _modName; set => SetField(ref _modName, value); }

    private string _mcVersion = "1.21.1";
    public string McVersion { get => _mcVersion; set => SetField(ref _mcVersion, value); }

    private string _loader = "Fabric";
    public string Loader { get => _loader; set => SetField(ref _loader, value); }

    public IReadOnlyList<string> Loaders { get; } = new[] { "Fabric", "Forge", "NeoForge", "Quilt" };

    private string _targetDir = "";
    public string TargetDir { get => _targetDir; set => SetField(ref _targetDir, value); }

    private bool _busy;
    public bool Busy { get => _busy; set => SetField(ref _busy, value); }

    private string _status = "填写信息后生成 Mod 骨架";
    public string Status { get => _status; set => SetField(ref _status, value); }

    public ICommand GenerateCommand => new AsyncRelayCommand(_ => GenerateAsync());

    /// <summary>选择目标目录（WPF 原生文件夹选择对话框）。</summary>
    public ICommand SelectDirCommand => new RelayCommand(_ => SelectDir());

    private void SelectDir()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "选择 Mod 工程目标目录" };
        if (dlg.ShowDialog() == true) TargetDir = dlg.FolderName;
    }

    private async Task GenerateAsync()
    {
        if (string.IsNullOrWhiteSpace(ModId) || string.IsNullOrWhiteSpace(TargetDir))
        {
            Status = "请填写 Mod ID 与目标目录";
            return;
        }

        Busy = true;
        Status = "正在生成骨架…";
        try
        {
            var root = Path.Combine(TargetDir, ModId);
            Directory.CreateDirectory(root);

            var javaPackage = "com." + ModId.ToLowerInvariant().Replace("-", "").Replace("_", "");
            var mainClass = $"{javaPackage}.{char.ToUpperInvariant(ModId[0]) + ModId.Substring(1).ToLowerInvariant().Replace("-", "")}";

            if (string.Equals(Loader, "Fabric", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Loader, "Quilt", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(Path.Combine(root, "fabric.mod.json"), FabricModJson(mainClass));
                WriteJavaStub(root, javaPackage, mainClass, "implements ModInitializer");
            }
            else
            {
                File.WriteAllText(Path.Combine(root, "src", "main", "resources", "META-INF", "mods.toml"), ForgeModsToml(mainClass));
                WriteJavaStub(root, javaPackage, mainClass, "implements IModContent");
            }

            Status = $"已生成到 {root}";
        }
        catch (Exception ex)
        {
            Status = $"生成失败：{ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private string FabricModJson(string mainClass) =>
        $$"""
        {
          "schemaVersion": 1,
          "id": "{{ModId}}",
          "version": "1.0.0",
          "name": "{{ModName}}",
          "environment": "*",
          "entrypoints": { "main": [ "{{mainClass}}" ] },
          "depends": { "fabricloader": ">=0.15.0", "minecraft": "{{McVersion}}" }
        }
        """;

    private string ForgeModsToml(string mainClass) =>
        $$"""
        modLoader="javafml"
        loaderVersion="[47,)"
        [[mods]]
        modId="{{ModId}}"
        version="1.0.0"
        displayName="{{ModName}}"
        [[mods]]
        modId="{{ModId}}"
        className="{{mainClass}}"
        """;

    private void WriteJavaStub(string root, string pkg, string cls, string impl)
    {
        var dir = Path.Combine(root, "src", "main", "java", pkg.Replace('.', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, cls + ".java"),
            $"package {pkg};\n\npublic class {cls} {impl} {{\n    @Override\n    public void onInitialize() {{\n        // TODO: 实现 Mod 逻辑\n    }}\n}}\n");
    }
}
