using System.Threading.Tasks;
using Chert.Core.Update;
using Chert.Core.Utils;

namespace Chert.App.Services;

/// <summary>
/// 更新检测编排：检查新版本（GET GitHub Pages 托管的 latest.json）→ 弹窗展示更新日志。
/// 供「设置页-检查更新」与「启动时自动检查」共用，避免逻辑重复。
/// 不误报保证：<see cref="LauncherUpdater.IsNewer"/> 在当前版本等于最新版本时返回 false，
/// 不会提示「更新到自身」（例如在 2.5.3 上不会弹出「更新到 2.5.3」）。
/// </summary>
public static class UpdateNotifier
{
    /// <summary>
    /// 执行一次更新检查；若发现新版本则弹出更新对话框。
    /// 任何网络/解析异常都被吞掉并写入 <see cref="UpdateCheckResult.Error"/>，不误报、不打断用户。
    /// </summary>
    public static async Task<UpdateCheckResult> CheckAndShowAsync()
    {
        UpdateCheckResult result;
        try
        {
            result = await LauncherUpdater.CheckAsync(GameConstants.LauncherVersion);
        }
        catch (System.Exception ex)
        {
            return new UpdateCheckResult { CurrentVersion = GameConstants.LauncherVersion, Error = ex.Message };
        }

        // 更新日志已在 CheckAsync 内随 latest.json 一并取回（result.Changelog）；有更新即弹窗。
        if (result.Available && string.IsNullOrEmpty(result.Error))
            UIService.ShowUpdateAvailable(result);

        return result;
    }
}
