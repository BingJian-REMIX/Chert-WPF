using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Chert.App.Services;
using Chert.App.Themes;
using Chert.Core.Localization;
using Chert.Core.Update;
using Chert.Core.Utils;

namespace Chert.App.Views;

/// <summary>
/// 更新可用时的模态弹窗：展示新版本号与更新日志（来自 GitHub Pages latest.json 的 changelog 字段），
/// 提供「下载更新」（调用启动器内置下载器拉取 CNB Release 下载直链，下载完成后生成 PowerShell 更新脚本，
/// 由脚本在退出旧进程后解压覆盖、删除压缩包并重启启动器）与「稍后」按钮。
/// 半透明遮罩覆盖整个主窗口，卡片居中（规格 1.4：弹窗模态居中、半透明遮罩、主操作按钮右置）。
/// </summary>
public partial class UpdateDialog : Window
{
    private readonly UpdateCheckResult _result;

    public UpdateDialog(UpdateCheckResult result)
    {
        InitializeComponent();
        _result = result;

        TitleText.Text = LocaleManager.T("update.found") + $" v{result.LatestVersion}";

        // 紧急更新（status=emgent）：显示红色横幅并置边框高亮，副标题强调立即安装。
        if (result.Status == "emgent")
        {
            EmergencyBanner.Visibility = Visibility.Visible;
            Card.BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
        }
        SubtitleText.Text = LocaleManager.Tf("update.version_line", result.CurrentVersion, result.LatestVersion) +
                            (result.Status == "emgent"
                                ? LocaleManager.T("update.emgent_note")
                                : (result.Mandatory ? LocaleManager.T("update.recommend_note") : ""));

        ChangelogBox.Markdown = string.IsNullOrWhiteSpace(result.Changelog)
            ? LocaleManager.T("update.no_changelog")
            : result.Changelog;

        // 让遮罩铺满 Owner 窗口，卡片在其上居中；并播放统一入场动画。
        Loaded += (_, _) =>
        {
            if (Owner is Window o && o.IsLoaded)
            {
                Left = o.Left;
                Top = o.Top;
                Width = o.ActualWidth;
                Height = o.ActualHeight;
            }
            AnimationHelper.PlayModalEnter(Card);
        };
    }

    /// <summary>点击「下载更新」：用启动器内置下载器拉取 CNB Release 下载直链，下载完成后生成 PowerShell 更新脚本并启动它，
    /// 随后退出当前进程；脚本在旧进程退出后解压覆盖安装目录、删除压缩包并重启启动器。</summary>
    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        // 当前启动器安装目录（供脚本覆盖替换用，并用于按安装形态自动选包）
        var installDir = Path.GetDirectoryName(
            Environment.ProcessPath ?? AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!;

        // 自动按当前安装形态选包：light（framework-dependent）版安装目录含 Chert.Core.dll，
        // 自包含（single-file）版所有类型已内联进 exe、无该 dll。light 包可用且存在时优先拉 light 包。
        var useLight = _result.LightAvailable
                       && !string.IsNullOrWhiteSpace(_result.LightDownloadUrl)
                       && File.Exists(Path.Combine(installDir, "Chert.Core.dll"));
        var url = useLight ? _result.LightDownloadUrl : _result.DownloadUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            TryOpenBrowser(GameConstants.GitHubRepoUrl + "/releases");
            Close();
            return;
        }

        DownloadButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;
        StatusText.Text = LocaleManager.T("update.fetching");

        var version = _result.LatestVersion ?? GameConstants.LauncherVersion;
        var updRoot = Path.Combine(Path.GetTempPath(), "Chert", "update");
        Directory.CreateDirectory(updRoot);
        var zipPath = Path.Combine(updRoot, $"Chert-v{version}-win-x64.zip");
        var scriptPath = Path.Combine(updRoot, "update.ps1");

        var progress = new Progress<double>(p =>
        {
            ProgressBar.Value = p;
            StatusText.Text = LocaleManager.Tf("update.downloading_pct", Math.Round(p * 100));
        });

        try
        {
            await LauncherService.Instance.DownloadFileAsync(url, zipPath, progress);

            // 当前启动器安装目录（供脚本覆盖替换用）
            installDir = Path.GetDirectoryName(
                Environment.ProcessPath ?? AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!;
            var exeName = Path.GetFileName(
                Environment.ProcessPath ?? AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar) + ".exe");
            var extractDir = Path.Combine(updRoot, version);

            // 生成 PowerShell 更新脚本：等待旧进程退出 → 解压 → 覆盖安装目录 → 删 zip → 重启
            var script = @"$ErrorActionPreference = 'Stop'
$zip     = '__ZIP__'
$extract = '__EXTRACT__'
$install = '__INSTALL__'
$exe     = '__EXE__'

# 等待旧启动器完全退出，释放被锁定的文件
Start-Sleep -Seconds 2

# 清理并解压
if (Test-Path -LiteralPath $extract) { Remove-Item -LiteralPath $extract -Recurse -Force }
Expand-Archive -Path $zip -DestinationPath $extract -Force

# 若 zip 内含单层根目录，则以其内容为准
$src = $extract
$top = Get-ChildItem -LiteralPath $extract
if ($top.Count -eq 1 -and $top[0].PSIsContainer) { $src = $top[0].FullName }

# 覆盖安装目录（程序文件），用户数据目录不受影响
Copy-Item -Path (Join-Path $src '*') -Destination $install -Recurse -Force

# 清理压缩包
Remove-Item -LiteralPath $zip -Force

# 重启启动器
Start-Process -FilePath $exe
"
                .Replace("__ZIP__", zipPath)
                .Replace("__EXTRACT__", extractDir)
                .Replace("__INSTALL__", installDir)
                .Replace("__EXE__", Path.Combine(installDir, exeName));

            File.WriteAllText(scriptPath, script);

            StatusText.Text = LocaleManager.T("update.applying");
            // 启动脚本（不等待），随后退出当前进程以释放文件锁，由脚本完成替换与重启
            Process.Start(new ProcessStartInfo("powershell",
                $"-ExecutionPolicy Bypass -File \"{scriptPath}\"") { UseShellExecute = true });
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            StatusText.Text = LocaleManager.Tf("update.failed", ex.Message);
            TryOpenBrowser(url);
            DownloadButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
        }
    }

    private static void TryOpenBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* 打开浏览器失败不影响弹窗 */ }
    }

    private void Later_Click(object sender, RoutedEventArgs e) => Close();
}
