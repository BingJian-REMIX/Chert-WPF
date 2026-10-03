using System.Diagnostics;
using System.Windows;
using Chert.Core.Launcher;
using Chert.Core.Mods;
using Chert.Core.Profiles;
using Chert.Core.Save;
using Chert.Core.Toolbox;
using Chert.Core.Utils;
using Chert.App.Views;
using Chert.App.ViewModels;
using Chert.App.Services;

namespace Chert.App.Services;

/// <summary>
/// 统一的游戏启动协调器：启动前存档兼容性检测、缺失前置自动安装、启动，
/// 以及崩溃自动修复循环（始终开启 / 每次询问 / 始终拒绝）。
/// 主页「快速启动」与「下载 → 版本列表」共用此逻辑，避免重复实现。
/// </summary>
public static class LaunchCoordinator
{
    public static async Task LaunchAsync(string versionId, Action<string>? status = null)
    {
        if (string.IsNullOrEmpty(versionId))
        {
            status?.Invoke("请先选择一个版本");
            return;
        }

        status?.Invoke($"正在启动 {versionId} …");
        try
        {
            var gameRoot = LauncherService.Instance.GameRoot;

            // §2.3-16 启动前文件变更检测（非阻塞 Toast，可查看详情）
            await CheckFileChangesAsync(status, versionId);

            // §二.4 启动前存档兼容性检测
            var incompatible = SaveCompatibilityDetector.Scan(gameRoot, versionId)
                .Where(r => !r.Compatible).ToList();
            if (incompatible.Count > 0)
            {
                var proceed = false;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var win = new SaveCompatPromptView(gameRoot, versionId, incompatible)
                    {
                        Owner = Application.Current.MainWindow
                    };
                    win.ShowDialog();
                    proceed = win.Proceed;
                });
                if (!proceed)
                {
                    status?.Invoke("已取消启动：存档版本高于目标游戏版本。");
                    return;
                }
            }

            // 启动前依赖检测（缺失前置按设置自动安装或提示）
            await CheckLaunchDependenciesAsync(versionId, status);

            // §2.3 音乐播放器联动：游戏启动时自动降音量 / 暂停
            MusicPlayerViewModel.Instance.OnGameLaunch();

            // HUD 叠加层已由 GameLauncher.GameProcessStarted 统一触发（见 App.xaml.cs 订阅），
            // 覆盖全部启动路径且无固定 1.5s 竞态，此处不再重复激活。

            var policy = ProfileStore.Load(gameRoot).RepairPolicy;

            // ★ problem3 多实例：**只启动、不等退出**。
            //   原先 await 到游戏退出才返回，会让调用方命令的 _isRunning 一直为 true ——
            //   启动按钮在整个游戏运行期间都是灰的，无法再开第二个实例。
            //   崩溃跟进（分析 / 自动修复 / 恢复音量）改为后台任务，能力完全保留，
            //   只是不再占用启动按钮。
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await LauncherService.Instance.LaunchAsync(versionId);
                    await HandleLaunchResult(result, versionId, policy, status);
                }
                catch (Exception ex)
                {
                    status?.Invoke($"启动失败：{ex.Message}");
                }
                finally
                {
                    // 游戏进程结束后恢复音量
                    MusicPlayerViewModel.Instance.OnGameExit();
                }
            });

            status?.Invoke("正在启动游戏…");
        }
        catch (Exception ex)
        {
            status?.Invoke($"启动失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 规格 2.3-16 / 3.13：后台自动检测手动丢入 mods/resourcepacks/shaderpacks 的新文件。
    /// 启动前（或启动器焦点回归时）调用；发现新增文件则弹右下角非阻塞 Toast 提示。
    /// 文件变更检测页已移除，此功能 purely 后台自动任务，开关保留在设置 → 通用。
    /// </summary>
    /// <summary>
    /// 文件变更检测（启动时调用）。
    /// <para><b>版本隔离适配</b>：<paramref name="versionId"/> 用于解析该版本**实际的工作目录**
    /// —— 隔离版查 <c>versions/&lt;id&gt;/</c>，共享版查 <c>gameRoot</c>。此前本方法
    /// 拿不到版本号，只能查共享位置，于是：隔离版装在自己目录里的 mods / config
    /// <b>永远检测不到</b>，而共享位置的变动会<b>误报到隔离版头上</b>。</para>
    /// </summary>
    public static async Task CheckFileChangesAsync(Action<string>? status = null, string? versionId = null)
    {
        if (!ProfileStore.Load(LauncherService.Instance.GameRoot).FileWatchEnabled)
            return;

        try
        {
            var gameRoot = LauncherService.Instance.GameRoot;

            // ★ 版本隔离适配（problem3 终版）：按该版本**实际的工作目录**检测。
            //   此前无条件传 gameRoot，等于「隔离版也去查共享位置」——
            //   隔离版装在 versions/<id>/ 的 mods / config 永远检测不到，
            //   而共享位置的文件变动会误报到隔离版头上。
            //   WatchDirFor 与启动时的 --gameDir 同源，隔离与共享不再互相干扰。
            // 调用方未指定版本时回落到「上次使用的版本」，避免退化成查共享位置
            var vid = versionId;
            if (string.IsNullOrEmpty(vid))
                vid = ProfileStore.Load(gameRoot).LastVersionId;
            var watchDir = FileChangeDetector.WatchDirFor(gameRoot, vid);

            // 两段式检测（对齐 MCLCS-Linux FileWatchService）：
            // ① 先比大小/修改时间，无变化直接返回（跳过昂贵的全量哈希）；
            // ② 仅对疑似变更的文件算 SHA-256 按内容确认，剔除 mtime 抖动误报。
            var diff = await Task.Run(() => FileChangeDetector.DetectTwoStage(watchDir));
            var added = FileChangeDetector.NewFilesOnly(diff);
            if (added.Count == 0) return;

            var preview = string.Join("、", added.Take(3).Select(c => c.Path));
            var more = added.Count > 3 ? $" 等共 {added.Count} 个文件" : "";
            ToastService.Show(
                "文件变更检测",
                $"检测到新增：{preview}{more}",
                ToastKind.Info);
        }
        catch (Exception ex)
        {
            status?.Invoke($"文件变更检测失败：{ex.Message}");
        }
    }

    private static async Task CheckLaunchDependenciesAsync(string versionId, Action<string>? status)
    {
        var pref = ProfileStore.Load(LauncherService.Instance.GameRoot).AutoInstallMissingMods;
        if (pref == AutoInstallPolicy.Never) return;

        // ★ 版本隔离适配（problem3 终版）：Mod 依赖检查必须查**该版本实际的工作目录**。
        //   此前一律传 GameRoot，等于「隔离版去查共享位置的 mods」——
        //   隔离版自己装的 mod 缺依赖报不出来，共享位置别人的 mod 缺失却会误报。
        //   同文件的存档兼容检测（上方）已正确传 versionId，此前两处逻辑不一致。
        var depDir = FileChangeDetector.WatchDirFor(LauncherService.Instance.GameRoot, versionId);
        var missing = ModManager.MissingDependencies(depDir);
        if (missing.Count == 0) return;

        if (pref == AutoInstallPolicy.Always)
        {
            var plan = new CrashRepairPlan
            {
                CanRepair = true,
                Strategy = RepairStrategy.InstallMissingModDependency,
                MissingModDependencies = missing,
                VersionId = versionId
            };
            var ok = await LauncherService.Instance.ApplyRepairAsync(plan);
            status?.Invoke(ok
                ? $"已自动安装 {missing.Count} 个缺失前置，继续启动…"
                : $"缺失前置自动安装失败（{missing.Count} 个），继续启动");
        }
        else
        {
            status?.Invoke($"检测到 {missing.Count} 个缺失前置，可在「Mod 管理」页一键安装");
        }
    }

    private static async Task HandleLaunchResult(LaunchResult result, string versionId,
        CrashRepairPolicy policy, Action<string>? status)
    {
        if (result.CrashReportPath is null)
        {
            status?.Invoke($"游戏进程已结束（退出码 {result.ExitCode}）");
            return;
        }

        var repairable = result.RepairPlan is { CanRepair: true };

        if (policy == CrashRepairPolicy.Never || !repairable)
        {
            ShowReport(result, allowRepair: false);
            status?.Invoke(repairable
                ? $"崩溃（退出码 {result.ExitCode}），按策略不自动修复"
                : $"崩溃（退出码 {result.ExitCode}），无法自动修复，已展示详情");
            return;
        }

        if (policy == CrashRepairPolicy.Always)
        {
            // 冲突触发的禁用（如 Mod 冲突）必须先弹窗让用户选择保留哪一个，
            // 即便策略为「始终」也不得静默执行（默认保留第一个）。
            if (result.RepairPlan is { Strategy: RepairStrategy.DisableConflictingMods })
            {
                ShowReport(result, allowRepair: true, versionId);
                status?.Invoke("检测到 Mod 冲突，已弹出选择窗口，请选择要保留的 Mod");
                return;
            }

            var cur = result;
            var attempts = 0;
            while (cur.CrashReportPath is not null
                   && cur.RepairPlan is { CanRepair: true }
                   && attempts < GameConstants.MaxRepairAttempts)
            {
                attempts++;
                status?.Invoke($"自动修复中（{cur.RepairPlan.Strategy}），第 {attempts} 次…");
                await LauncherService.Instance.ApplyRepairAsync(cur.RepairPlan);
                cur = await LauncherService.Instance.LaunchAsync(versionId);
            }

            if (cur.CrashReportPath is null)
                status?.Invoke("自动修复成功，游戏已正常启动。");
            else
            {
                ShowReport(cur, allowRepair: cur.RepairPlan is { CanRepair: true });
                status?.Invoke(!repairable || cur.RepairPlan is not { CanRepair: true }
                    ? "已尝试自动修复但仍崩溃，且无法继续自动修复。"
                    : $"已达最大自动修复次数（{GameConstants.MaxRepairAttempts}）。");
            }
            return;
        }

        // 每次询问
        ShowReport(result, allowRepair: true, versionId);
        status?.Invoke($"崩溃（退出码 {result.ExitCode}），可尝试自动修复。");
    }

    private static void ShowReport(LaunchResult result, bool allowRepair, string? versionId = null)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Func<LaunchResult, Task<LaunchResult?>> relaunch = async r =>
            {
                if (r.RepairPlan is null) return r;
                await LauncherService.Instance.ApplyRepairAsync(r.RepairPlan);
                var vid = versionId ?? ProfileStore.Load(LauncherService.Instance.GameRoot).LastVersionId;
                if (string.IsNullOrEmpty(vid)) return r;
                var res = await LauncherService.Instance.LaunchAsync(vid);
                return res.CrashReportPath is null ? null : res;
            };

            var win = new CrashReportView(result, relaunch, allowRepair)
            {
                Owner = Application.Current.MainWindow
            };
            win.Show();
        });
    }
}
