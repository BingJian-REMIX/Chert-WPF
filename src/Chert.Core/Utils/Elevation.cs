using System.Diagnostics;
using System.Security.Principal;

namespace Chert.Core.Utils;

/// <summary>管理员权限检测与提权（Windows）。非 Windows 平台视为已提权。</summary>
public static class Elevation
{
    /// <summary>当前进程是否为管理员。</summary>
    public static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows()) return true;
        try
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>以管理员身份重启当前程序（UAC 提权）。返回是否成功发起。</summary>
    public static bool RestartAsAdmin(string? arguments = null)
    {
        if (!OperatingSystem.IsWindows()) return false;

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            Verb = "runas"
        };
        if (!string.IsNullOrEmpty(arguments))
            psi.Arguments = arguments;

        try
        {
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
