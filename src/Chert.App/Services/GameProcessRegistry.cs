using System.Diagnostics;

namespace Chert.App.Services;

/// <summary>
/// 当前由启动器拉起的游戏进程登记表。
/// <para>局域网联动 / HUD / 触屏面板都需要拿到游戏窗口句柄，
/// 与其各自去猜 <c>javaw.exe</c>，不如启动时登记一次、退出时清空。</para>
/// </summary>
public static class GameProcessRegistry
{
    private static Process? _current;
    private static readonly object Gate = new();

    /// <summary>当前游戏进程；已退出或未启动时为 null。</summary>
    public static Process? Current
    {
        get
        {
            lock (Gate)
            {
                if (_current is null) return null;
                try
                {
                    if (_current.HasExited) { _current = null; return null; }
                }
                catch
                {
                    _current = null;
                    return null;
                }
                return _current;
            }
        }
    }

    /// <summary>登记游戏进程。</summary>
    public static void Set(Process? proc)
    {
        lock (Gate) { _current = proc; }
    }

    /// <summary>清空登记（游戏退出时）。</summary>
    public static void ClearIfSame(Process? proc)
    {
        lock (Gate)
        {
            if (proc is null || ReferenceEquals(_current, proc)) _current = null;
        }
    }
}
