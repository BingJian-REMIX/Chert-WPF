using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Chert.Core.Profiles;
using Chert.Core.Utils;

namespace Chert.App.Services;

/// <summary>
/// 清单 #63：全局快捷键（系统级，启动器不在前台也生效）。
/// 采用 Win32 RegisterHotKey：注册到主窗口句柄，由窗口过程接收 WM_HOTKEY。
/// 目前提供两个默认组合：
///   Ctrl+Alt+Enter —— 快速启动上次游玩的版本
///   Ctrl+Alt+M     —— 呼出 / 收起启动器窗口
/// 注册失败（组合被其它程序占用等）一律静默忽略，不影响启动器正常使用。
/// </summary>
public static class GlobalHotkeyService
{
    private const int WmHotkey = 0x0312;

    private const int ModAlt = 0x0001;
    private const int ModControl = 0x0002;
    private const int ModNoRepeat = 0x4000;

    private const uint VkReturn = 0x0D;
    private const uint VkM = 0x4D;

    private const int IdQuickLaunch = 0x6C01;
    private const int IdToggleWindow = 0x6C02;

    private static HwndSource? _source;
    private static IntPtr _hwnd;
    private static bool _registered;

    /// <summary>最近一次注册结果说明（供设置页提示）。</summary>
    public static string StatusText { get; private set; } = "";

    /// <summary>是否启用（来自 profile.GlobalHotkeysEnabled）。</summary>
    public static bool Enabled { get; private set; }

    /// <summary>在主窗口 Loaded 时调用：按 profile 开关注册全局热键。</summary>
    public static void Attach(System.Windows.Window window)
    {
        try
        {
            Detach();

            var profile = ProfileStore.Load(GameConstants.DefaultGameRoot);
            Enabled = profile.GlobalHotkeysEnabled;
            if (!Enabled) { StatusText = "全局快捷键已关闭"; return; }

            _hwnd = new WindowInteropHelper(window).Handle;
            if (_hwnd == IntPtr.Zero) { StatusText = "窗口句柄尚未就绪"; return; }

            _source = HwndSource.FromHwnd(_hwnd);
            if (_source is null) { StatusText = "无法挂接窗口消息"; return; }
            _source.AddHook(WndProc);

            var mods = ModAlt | ModControl | ModNoRepeat;
            var okLaunch = RegisterHotKey(_hwnd, IdQuickLaunch, mods, VkReturn);
            var okToggle = RegisterHotKey(_hwnd, IdToggleWindow, mods, VkM);
            _registered = true;

            StatusText = (okLaunch, okToggle) switch
            {
                (true, true) => "全局快捷键已启用：Ctrl+Alt+Enter 快速启动 / Ctrl+Alt+M 呼出启动器",
                (true, false) => "快速启动已注册；Ctrl+Alt+M 被其它程序占用",
                (false, true) => "呼出启动器已注册；Ctrl+Alt+Enter 被其它程序占用",
                _ => "快捷键组合均被占用，未启用全局快捷键"
            };
        }
        catch (Exception ex)
        {
            StatusText = $"全局快捷键注册失败：{ex.Message}";
        }
    }

    /// <summary>窗口关闭 / 设置变更时注销。</summary>
    public static void Detach()
    {
        try
        {
            if (_source is not null)
            {
                _source.RemoveHook(WndProc);
                _source = null;
            }
            if (_hwnd != IntPtr.Zero && _registered)
            {
                UnregisterHotKey(_hwnd, IdQuickLaunch);
                UnregisterHotKey(_hwnd, IdToggleWindow);
            }
        }
        catch { /* 注销失败无需处理 */ }
        finally
        {
            _registered = false;
            _hwnd = IntPtr.Zero;
        }
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey) return IntPtr.Zero;

        var id = wParam.ToInt32();
        handled = true;

        switch (id)
        {
            case IdQuickLaunch:
                QuickLaunch();
                break;
            case IdToggleWindow:
                ToggleWindow();
                break;
        }

        return IntPtr.Zero;
    }

    /// <summary>快速启动上次游玩的版本（无记录时不动作）。</summary>
    private static void QuickLaunch()
    {
        try
        {
            var profile = ProfileStore.Load(GameConstants.DefaultGameRoot);
            var id = profile.LastVersionId;
            if (string.IsNullOrWhiteSpace(id))
            {
                ToastService.Show("快速启动", "尚未游玩过任何版本，无法快速启动", ToastKind.Warning);
                return;
            }

            ToastService.Show("快速启动", $"正在启动 {id}…");
            _ = LauncherService.Instance.LaunchAsync(id!);
        }
        catch (Exception ex)
        {
            ToastService.Show("快速启动", $"启动失败：{ex.Message}", ToastKind.Error);
        }
    }

    /// <summary>呼出（最小化时恢复并置前）或收起启动器窗口。</summary>
    private static void ToggleWindow()
    {
        try
        {
            var window = Application.Current.MainWindow;
            if (window is null) return;

            window.Dispatcher.Invoke(() =>
            {
                if (window.WindowState == WindowState.Minimized || !window.IsVisible)
                {
                    window.Show();
                    window.WindowState = WindowState.Normal;
                    window.Activate();
                    window.Topmost = true;
                    window.Topmost = false;
                }
                else
                {
                    window.WindowState = WindowState.Minimized;
                }
            });
        }
        catch { /* 忽略 */ }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
