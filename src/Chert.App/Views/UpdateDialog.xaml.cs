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
///
/// ★ GUI / CLI 分离包（2026-10-03）：过去一个 zip 里塞了 GUI + CLI 两个自包含 exe
/// （各带一份 .NET 运行时，2.6.0 实测合体 110.86 MB）。现改为三个独立包，
/// 弹窗提供「只更新界面」与「全部更新」两个入口 —— 只用界面的用户不必下载 CLI。
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

        // ── GUI / CLI 分离包 ────────────────────────────────────────────
        // 没提供 CLI 包时隐藏「只更新界面」（此时它与「全部更新」等价），
        // 避免按钮行出现两个一模一样的选项。
        var hasCli = !string.IsNullOrWhiteSpace(result.CliDownloadUrl);
        var hasGui = !string.IsNullOrWhiteSpace(result.GuiDownloadUrl)
                     || !string.IsNullOrWhiteSpace(result.GuiLightDownloadUrl)
                     || !string.IsNullOrWhiteSpace(result.DownloadUrl);
        GuiOnlyButton.Visibility = hasCli && hasGui ? Visibility.Visible : Visibility.Collapsed;
        DownloadButton.Content = hasCli
            ? LocaleManager.T("update.download_all")
            : LocaleManager.T("update.download");

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

    /// <summary>取第一个非空字符串（用于「新字段优先、旧字段兜底」的兼容取值）。</summary>
    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var c in candidates)
            if (!string.IsNullOrWhiteSpace(c)) return c;
        return null;
    }

    /// <summary>「只更新界面」：仅拉启动器界面所在的包（按当前安装形态选自包含 / 轻量），
    /// 不下载 CLI 包。绝大多数用户只用界面，这样能显著减少下载量。</summary>
    private async void GuiOnly_Click(object sender, RoutedEventArgs e)
        => await StartUpdateAsync(includeCli: false);

    /// <summary>「全部更新」：GUI + CLI 两个包都拉。</summary>
    private async void Download_Click(object sender, RoutedEventArgs e)
        => await StartUpdateAsync(includeCli: true);

    /// <summary>
    /// 拉取更新包并生成 PowerShell 更新脚本，随后退出当前进程；
    /// 脚本在旧进程退出后解压覆盖安装目录、删除压缩包并重启启动器。
    /// </summary>
    /// <param name="includeCli">是否连 CLI 包一起更新（对应弹窗两个按钮）。</param>
    private async Task StartUpdateAsync(bool includeCli)
    {
        // 当前启动器安装目录（供脚本覆盖替换用，并用于按安装形态自动选包）
        var installDir = Path.GetDirectoryName(
            Environment.ProcessPath ?? AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!;

        // ★ 按当前安装形态选包：light（framework-dependent）版安装目录含 Chert.Core.dll；
        //   自包含（single-file）版所有类型已内联进 exe、无该 dll。
        //   → 命中 light 就拉轻量包，否则拉自包含包。
        //   新字段（gui*）优先，旧字段（downloadUrl/lightDownloadUrl）兜底以兼容旧清单。
        var useLight = File.Exists(Path.Combine(installDir, "Chert.Core.dll"));
        _result.SelfContainedInstall = !useLight;

        var guiUrl = useLight
            ? FirstNonEmpty(_result.GuiLightDownloadUrl, _result.LightDownloadUrl, _result.GuiDownloadUrl)
            : FirstNonEmpty(_result.GuiDownloadUrl, _result.DownloadUrl);

        // 「全部更新」且提供了 CLI 包时，第二个包就是它
        var cliUrl = includeCli ? _result.CliDownloadUrl : null;

        if (string.IsNullOrWhiteSpace(guiUrl))
        {
            TryOpenBrowser(GameConstants.GitHubRepoUrl + "/releases");
            Close();
            return;
        }

        DownloadButton.IsEnabled = false;
        GuiOnlyButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;
        StatusText.Text = LocaleManager.T("update.fetching");

        var version = _result.LatestVersion ?? GameConstants.LauncherVersion;
        var updRoot = Path.Combine(Path.GetTempPath(), "Chert", "update");
        Directory.CreateDirectory(updRoot);
        var guiZip = Path.Combine(updRoot, $"Chert-gui-v{version}-win-x64.zip");
        var cliZip = string.IsNullOrWhiteSpace(cliUrl)
            ? null
            : Path.Combine(updRoot, $"chert-cli-v{version}-win-x64.zip");
        var scriptPath = Path.Combine(updRoot, "update.ps1");

        var progress = new Progress<double>(p =>
        {
            ProgressBar.Value = p;
            StatusText.Text = LocaleManager.Tf("update.downloading_pct", Math.Round(p * 100));
        });

        try
        {
            await LauncherService.Instance.DownloadFileAsync(guiUrl, guiZip, progress);
            if (cliZip is not null)
            {
                // CLI 包单独下载，进度条保持满格并改文案，避免用户以为卡住
                StatusText.Text = LocaleManager.T("update.fetching_cli");
                var cliProgress = new Progress<double>(p =>
                {
                    ProgressBar.Value = 1;
                    StatusText.Text = LocaleManager.Tf("update.downloading_cli_pct", Math.Round(p * 100));
                });
                await LauncherService.Instance.DownloadFileAsync(cliUrl!, cliZip, cliProgress);
            }

            // 当前启动器安装目录（供脚本覆盖替换用）
            installDir = Path.GetDirectoryName(
                Environment.ProcessPath ?? AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!;
            var exeName = Path.GetFileName(
                Environment.ProcessPath ?? AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar) + ".exe");
            var extractDir = Path.Combine(updRoot, version);

            // 生成 PowerShell 更新脚本：等待旧进程退出 → 解压 → 覆盖安装目录 → 删 zip → 重启
            // GUI / CLI 分离包：两个 zip 各自解压后**合并**覆盖到安装目录。
            // 只有一个包时 $cliZip 为空字符串，Expand-One 会直接跳过。
            var script = @"$ErrorActionPreference = 'Stop'
$guiZip  = '__GUIZIP__'
$cliZip  = '__CLIZIP__'
$extract = '__EXTRACT__'
$install = '__INSTALL__'
$exe     = '__EXE__'

# 等待旧启动器完全退出，释放被锁定的文件
Start-Sleep -Seconds 2

if (Test-Path -LiteralPath $extract) { Remove-Item -LiteralPath $extract -Recurse -Force }
New-Item -ItemType Directory -Path $extract -Force | Out-Null

function Expand-One($zipPath, $destRoot) {
    if ([string]::IsNullOrWhiteSpace($zipPath)) { return }
    if (-not (Test-Path -LiteralPath $zipPath)) { return }
    $stage = Join-Path $destRoot ([System.IO.Path]::GetFileNameWithoutExtension($zipPath))
    Expand-Archive -Path $zipPath -DestinationPath $stage -Force
    # 若 zip 内含单层根目录，则以其内容为准
    $src = $stage
    $top = Get-ChildItem -LiteralPath $stage
    if ($top.Count -eq 1 -and $top[0].PSIsContainer) { $src = $top[0].FullName }
    Copy-Item -Path (Join-Path $src '*') -Destination $destRoot -Recurse -Force
}

# GUI 包（必下）
Expand-One $guiZip $extract
# CLI 包（仅「全部更新」时存在）
Expand-One $cliZip $extract

# 覆盖安装目录（程序文件），用户数据目录不受影响
Copy-Item -Path (Join-Path $extract '*') -Destination $install -Recurse -Force

# 清理压缩包与暂存
if (-not [string]::IsNullOrWhiteSpace($guiZip)) { Remove-Item -LiteralPath $guiZip -Force -ErrorAction SilentlyContinue }
if (-not [string]::IsNullOrWhiteSpace($cliZip)) { Remove-Item -LiteralPath $cliZip -Force -ErrorAction SilentlyContinue }
Remove-Item -LiteralPath $extract -Recurse -Force -ErrorAction SilentlyContinue

# 重启启动器
Start-Process -FilePath $exe
"
                .Replace("__GUIZIP__", guiZip)
                .Replace("__CLIZIP__", cliZip ?? "")
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
            TryOpenBrowser(guiUrl ?? GameConstants.CnbReleaseBase);
            DownloadButton.IsEnabled = true;
            GuiOnlyButton.IsEnabled = true;
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
