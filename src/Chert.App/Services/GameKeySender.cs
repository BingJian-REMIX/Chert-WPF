using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Chert.App.Services;

/// <summary>
/// 清单 #11：向游戏窗口发送虚拟按键。
/// 优先用 PostMessage 直接投递到游戏窗口（Minecraft 的 GLFW 通过窗口消息取键，
/// 因此即使触屏面板抢到焦点也能生效）；拿不到窗口句柄时回退到全局 keybd_event。
/// </summary>
public static class GameKeySender
{
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_CHAR = 0x0102;

    private const int VK_T = 0x54;
    private const int VK_RETURN = 0x0D;

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>取游戏主窗口句柄；进程刚启动时可能尚未创建窗口，故轮询等待若干秒。</summary>
    public static IntPtr FindGameWindow(Process? proc, int timeoutMs = 8000)
    {
        if (proc is null) return IntPtr.Zero;
        var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
        while (DateTime.Now < deadline)
        {
            try
            {
                if (proc.HasExited) return IntPtr.Zero;
                proc.Refresh();
                var h = proc.MainWindowHandle;
                if (h != IntPtr.Zero) return h;
            }
            catch
            {
                return IntPtr.Zero;
            }
            Thread.Sleep(200);
        }
        return IntPtr.Zero;
    }

    /// <summary>按下（长按类按键在抬起前持续按住）。</summary>
    public static void KeyDown(IntPtr hwnd, int vk)
    {
        if (vk <= 0) return;
        if (hwnd != IntPtr.Zero)
        {
            var scan = MapVirtualKey((uint)vk, 0);
            var lParam = (IntPtr)(1 | (scan << 16));
            PostMessage(hwnd, WM_KEYDOWN, (IntPtr)vk, lParam);
            return;
        }
        try { keybd_event((byte)vk, 0, 0, UIntPtr.Zero); } catch { }
    }

    /// <summary>抬起。</summary>
    public static void KeyUp(IntPtr hwnd, int vk)
    {
        if (vk <= 0) return;
        if (hwnd != IntPtr.Zero)
        {
            var scan = MapVirtualKey((uint)vk, 0);
            // bit31 转换状态 + bit30 先前状态：置 1 表示「释放」
            var lParam = (IntPtr)(1 | (scan << 16) | (1 << 30) | (1 << 31));
            PostMessage(hwnd, WM_KEYUP, (IntPtr)vk, lParam);
            return;
        }
        try { keybd_event((byte)vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); } catch { }
    }

    /// <summary>按下后立刻抬起（动作键）。</summary>
    public static void Tap(IntPtr hwnd, int vk)
    {
        KeyDown(hwnd, vk);
        Thread.Sleep(30);
        KeyUp(hwnd, vk);
    }

    /// <summary>
    /// 逐字符投递 WM_CHAR 输入文本。
    /// <para>只处理 ASCII：游戏内命令（<c>/publish</c> 之类）用不到非 ASCII，
    /// 而 WM_CHAR 对非 ASCII 在不同输入法状态下的表现不可靠，直接跳过更安全。</para>
    /// </summary>
    public static void SendText(IntPtr hwnd, string text)
    {
        if (hwnd == IntPtr.Zero || string.IsNullOrEmpty(text)) return;
        foreach (var ch in text)
        {
            if (ch > 0x7F) continue;
            PostMessage(hwnd, WM_CHAR, (IntPtr)ch, IntPtr.Zero);
            Thread.Sleep(8);
        }
    }

    /// <summary>打开聊天框 → 输入命令 → 回车（用于 /publish 等）。</summary>
    public static void SendChatCommand(IntPtr hwnd, string command)
    {
        if (hwnd == IntPtr.Zero || string.IsNullOrWhiteSpace(command)) return;
        Tap(hwnd, VK_T);
        Thread.Sleep(180);
        SendText(hwnd, command);
        Thread.Sleep(80);
        Tap(hwnd, VK_RETURN);
    }
}
