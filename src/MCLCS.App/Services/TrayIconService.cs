using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace MCLCS.App.Services;

/// <summary>
/// 系统托盘图标服务（bug #26：最小化到托盘）。
/// 项目刻意不启用 UseWindowsForms（避免与 WPF 类型撞名 CS0104），故直接用 Win32
/// Shell_NotifyIcon + 一个不可见 HwndSource 消息窗口接收回调，零额外依赖。
/// 托盘图标优先从程序目录加载应用图标 MCLCS.ico（LoadImage + LR_LOADFROMFILE，多分辨率自适应）。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly HwndSource _hwndSource;
    private IntPtr _hIcon;
    private readonly ContextMenu _menu;
    private readonly Action _showWindow;
    private bool _disposed;
    private const int TrayId = 0x1;

    private static class Native
    {
        public const uint NIM_ADD = 0x00000000;
        public const uint NIM_DELETE = 0x00000002;
        public const uint NIF_MESSAGE = 0x00000001;
        public const uint NIF_ICON = 0x00000002;
        public const uint NIF_TIP = 0x00000004;
        public const uint WM_TRAYICON = 0x0400 + 1;
        public const uint IMAGE_ICON = 1;
        public const uint LR_LOADFROMFILE = 0x00000010;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_LBUTTONDBLCLK = 0x0203;
        public const int WM_RBUTTONUP = 0x0205;
        public const int WM_CONTEXTMENU = 0x007B;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public uint uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }
    }

    public TrayIconService(Window owner, Action showWindow, Action exitApp)
    {
        _showWindow = showWindow;

        // 不可见消息窗口：仅用于接收托盘回调，不显示、不入任务栏（WS_POPUP）。
        var param = new HwndSourceParameters("MCLCS_TraySink")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP
            Width = 0,
            Height = 0
        };
        _hwndSource = new HwndSource(param);
        _hwndSource.AddHook(WndProc);

        _hIcon = LoadTrayIcon();

        var data = new Native.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<Native.NOTIFYICONDATA>(),
            hWnd = _hwndSource.Handle,
            uID = TrayId,
            uFlags = Native.NIF_MESSAGE | Native.NIF_TIP | (_hIcon != IntPtr.Zero ? Native.NIF_ICON : 0u),
            uCallbackMessage = (int)Native.WM_TRAYICON,
            hIcon = _hIcon,
            szTip = "朗希夏 启动器"
        };
        Native.Shell_NotifyIcon(Native.NIM_ADD, ref data);

        _menu = BuildMenu(owner, exitApp);
    }

    private static IntPtr LoadTrayIcon()
    {
        foreach (var candidate in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "MCLCS.ico"),
            Path.Combine(AppContext.BaseDirectory, "Resources", "MCLCS.ico")
        })
        {
            try
            {
                if (File.Exists(candidate))
                {
                    var h = Native.LoadImage(IntPtr.Zero, candidate, Native.IMAGE_ICON, 0, 0, Native.LR_LOADFROMFILE);
                    if (h != IntPtr.Zero) return h;
                }
            }
            catch { /* 忽略损坏/缺失的图标，留空也可运行 */ }
        }
        return IntPtr.Zero;
    }

    private ContextMenu BuildMenu(Window owner, Action exitApp)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = owner,
            Placement = PlacementMode.MousePoint
        };
        var open = new MenuItem { Header = "打开主界面" };
        open.Click += (_, _) => _showWindow();
        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => exitApp();
        menu.Items.Add(open);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);
        return menu;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (int)Native.WM_TRAYICON)
        {
            var mouse = (int)lParam;
            if (mouse is Native.WM_LBUTTONDBLCLK or Native.WM_LBUTTONUP)
                _showWindow();
            else if (mouse is Native.WM_RBUTTONUP or Native.WM_CONTEXTMENU)
                _menu.IsOpen = true;
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var data = new Native.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<Native.NOTIFYICONDATA>(),
            hWnd = _hwndSource.Handle,
            uID = TrayId
        };
        try { Native.Shell_NotifyIcon(Native.NIM_DELETE, ref data); }
        catch { /* 托盘已不可用时忽略 */ }

        if (_hIcon != IntPtr.Zero)
        {
            Native.DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
        _hwndSource.Dispose();
    }
}
