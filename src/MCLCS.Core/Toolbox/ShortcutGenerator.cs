using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MCLCS.Core.Toolbox;

/// <summary>快捷方式生成结果。</summary>
public class ShortcutResult
{
    public bool Success { get; set; }
    public string? FilePath { get; set; }
    public string Method { get; set; } = ""; // lnk | bat | desktop
    public string? Error { get; set; }
}

/// <summary>
/// 快捷方式生成器（工具箱功能 7 / 全局功能 10）：为任意版本创建桌面快捷方式，
/// 双击即用该版本启动游戏（向启动器传入 <c>--launch &lt;versionId&gt;</c>）。
/// Windows 优先生成真正的 .lnk（通过 WSH COM，失败回退 .bat）；其他平台生成 .desktop / .sh。
/// </summary>
public static class ShortcutGenerator
{
    /// <summary>启动器主程序路径（运行时为当前进程映像）。</summary>
    public static string LauncherExe()
        => Process.GetCurrentProcess().MainModule?.FileName ?? "Chert Launcher.exe";

    /// <summary>为指定版本生成启动参数。</summary>
    public static string TargetArguments(string versionId) => $"--launch {versionId}";

    /// <summary>
    /// 在 <paramref name="desktopDir"/> 创建指向该版本的快捷方式。
    /// 返回结果（含生成的文件路径）。所有异常都被捕获，不会向外抛出。
    /// </summary>
    public static ShortcutResult CreateShortcut(string desktopDir, string versionId,
        string? displayName = null, string? iconPath = null)
    {
        try
        {
            Directory.CreateDirectory(desktopDir);
            var name = string.IsNullOrWhiteSpace(displayName) ? $"MCLCS - {versionId}" : displayName;
            var target = LauncherExe();
            var args = TargetArguments(versionId);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var lnk = Path.Combine(desktopDir, name + ".lnk");
                if (TryCreateLnk(lnk, target, args, desktopDir, iconPath))
                    return new ShortcutResult { Success = true, FilePath = lnk, Method = "lnk" };
                // 回退：写 .bat
                var bat = Path.Combine(desktopDir, name + ".bat");
                File.WriteAllText(bat,
                    $"@echo off\r\n\"{target}\" {args}\r\n", System.Text.Encoding.Default);
                return new ShortcutResult { Success = true, FilePath = bat, Method = "bat" };
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var desktop = Path.Combine(desktopDir, Sanitize(name) + ".desktop");
                File.WriteAllText(desktop,
                    $"[Desktop Entry]\nType=Application\nName={name}\nExec={target} {args}\n" +
                    (string.IsNullOrEmpty(iconPath) ? "" : $"Icon={iconPath}\n") +
                    "Terminal=false\n");
                return new ShortcutResult { Success = true, FilePath = desktop, Method = "desktop" };
            }

            // macOS / 其他：脚本
            var sh = Path.Combine(desktopDir, Sanitize(name) + ".sh");
            File.WriteAllText(sh, $"#!/bin/sh\n\"{target}\" {args}\n");
            return new ShortcutResult { Success = true, FilePath = sh, Method = "sh" };
        }
        catch (Exception ex)
        {
            return new ShortcutResult { Success = false, Error = ex.Message };
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool TryCreateLnk(string path, string target, string args, string workingDir, string? iconPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return false;
            dynamic shell = Activator.CreateInstance(shellType)!;
            var shortcut = shell.CreateShortcut(path);
            shortcut.TargetPath = target;
            shortcut.Arguments = args;
            shortcut.WorkingDirectory = workingDir;
            if (!string.IsNullOrEmpty(iconPath)) shortcut.IconLocation = iconPath;
            shortcut.Save();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Sanitize(string name) => new string(
        name.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray()).Trim();
}
