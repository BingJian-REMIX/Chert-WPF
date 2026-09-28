using Microsoft.Win32;

namespace Chert.App.Services;

/// <summary>
/// 开机自启：通过 HKCU 的 <c>Run</c> 注册表项登记启动器自身路径。
/// <para>
/// 之所以写 HKCU 而非 HKLM：HKLM 需要管理员权限，普通双击启动的启动器无法写入；
/// HKCU 对当前用户生效且无需提权，是启动器类程序的标准做法。
/// </para>
/// <para>所有操作都吞异常——注册表被策略禁用 / 杀软拦截都不能影响启动器使用。</para>
/// </summary>
public static class AutoStartService
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>注册表项名称（品牌名，卸载 / 改名时据此定位）。</summary>
    private const string ValueName = "Chert Launcher";

    /// <summary>当前启动器可执行文件路径（单文件发布下 <c>Environment.ProcessPath</c> 仍指向真实 exe）。</summary>
    public static string ExePath => Environment.ProcessPath ?? "";

    /// <summary>是否已登记开机自启。</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>写入当前 exe 路径；路径为空（如极端宿主环境）时不写入。</summary>
    public static void Enable()
    {
        var exe = ExePath;
        if (string.IsNullOrWhiteSpace(exe)) return;
        if (!File.Exists(exe)) return;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            // 路径含空格时引号保证能被正确解析
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        catch
        {
            // 写注册表失败（权限 / 组策略 / 安全软件）时静默放弃
        }
    }

    /// <summary>移除开机自启登记项。</summary>
    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            // 只有值确实存在时才删，避免对不存在的值做无效写入
            if (key?.GetValue(ValueName) is null) return;
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // 同上，失败静默
        }
    }

    /// <summary>按开关状态同步注册表。</summary>
    public static void Apply(bool enabled)
    {
        if (enabled) Enable();
        else Disable();
    }
}
