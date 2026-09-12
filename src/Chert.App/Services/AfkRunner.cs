using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Chert.Core.Tokens;

namespace Chert.App.Services;

/// <summary>AFK 运行进度快照（供 UI 展示当前轮次 / 步骤 / 已用时长）。</summary>
public sealed class AfkRunProgress
{
    public int StepIndex { get; init; }
    public int TotalSteps { get; init; }
    public string CurrentStep { get; init; } = "";
    public int Cycle { get; init; }
    public int TotalCycles { get; init; }
    public TimeSpan Elapsed { get; init; }
    public bool Running { get; init; }
}

/// <summary>
/// AFK 工作流执行引擎（bug #14）：把 <see cref="AfkWorkflowToken"/> 解析出的宏指令，
/// 通过 Windows <c>SendInput</c> 派发到目标（默认正在运行的 MC 窗口），
/// 支持延迟 / 长按 / 连点 / 整体循环 / 右键连点 / 鼠标移动 / 滚轮 / 文本输入 / 按住-松开 / 随机等待，
/// 并可随时取消。停止时强制释放所有「按住」的键，避免 MC 卡在持续前进/按下状态。
/// <para>此前两套 Token 字母表互不兼容且没有任何执行路径被接上，故运行时实际“不动作”。</para>
/// </summary>
public static class AfkRunner
{
    // ---- Win32 P/Invoke ----
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    /// <summary>
    /// 运行一段 AFK Token。<paramref name="targetPid"/> 为 MC 进程 PID（为空则用当前前台窗口）。
    /// <paramref name="progress"/> 回调在 UI 线程上报告进度；<paramref name="ct"/> 可取消。
    /// </summary>
    public static async Task RunAsync(string token, int? targetPid, IProgress<AfkRunProgress>? progress, CancellationToken ct)
    {
        var result = AfkWorkflowToken.Parse(token);
        if (!result.Ok)
            throw new InvalidOperationException(result.Error ?? "Token 非法");

        var hwnd = ResolveTargetWindow(targetPid);
        if (hwnd != IntPtr.Zero)
        {
            BringToFront(hwnd);
            // 等待前台窗口焦点真正切到 MC，否则 SendInput 会发到启动器自身
            await Task.Delay(150, ct);
        }

        var actions = result.Actions.ToList();
        var totalCycles = result.IsInfinite ? int.MaxValue : Math.Max(1, result.RepeatCount);
        var sw = Stopwatch.StartNew();
        var cycle = 0;
        var held = new HashSet<ushort>();

        try
        {
            do
            {
                cycle++;
                for (var i = 0; i < actions.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var ins = actions[i];
                    progress?.Report(new AfkRunProgress
                    {
                        StepIndex = i + 1,
                        TotalSteps = actions.Count,
                        CurrentStep = ins.Describe(),
                        Cycle = cycle,
                        TotalCycles = result.IsInfinite ? 0 : result.RepeatCount,
                        Elapsed = sw.Elapsed,
                        Running = true
                    });
                    await ExecuteAsync(ins, actions, i, ct, held);
                }
            } while (cycle < totalCycles && !ct.IsCancellationRequested);
        }
        finally
        {
            // 关键：无论正常结束还是取消，强制释放所有「按住」的键，避免卡键。
            foreach (var vk in held) SendKey(vk, false);
            held.Clear();
        }
    }

    private static async Task ExecuteAsync(AfkInstruction ins, List<AfkInstruction> actions, int index, CancellationToken ct, HashSet<ushort> held)
    {
        switch (ins.Kind)
        {
            case AfkOpKind.Delay:
                await Task.Delay(Math.Max(0, ins.A) * 1000, ct);
                break;

            case AfkOpKind.FunctionKey:
                SendKey(GetVk(ins), true);
                await Task.Delay(40, ct);
                SendKey(GetVk(ins), false);
                break;

            case AfkOpKind.KeyCode:
                SendKey(GetVk(ins), true);
                await Task.Delay(40, ct);
                SendKey(GetVk(ins), false);
                break;

            case AfkOpKind.LongPress:
            {
                // 长按作用于上一条按键 / 功能键指令
                var prev = actions.Take(index)
                    .LastOrDefault(x => x.Kind is AfkOpKind.FunctionKey or AfkOpKind.KeyCode);
                if (prev is null)
                {
                    // 没有可长按的键：退化为一次短按，避免静默丢失
                    await Task.Delay(ins.A * 1000, ct);
                    break;
                }
                var vk = GetVk(prev);
                SendKey(vk, true);
                try { await Task.Delay(ins.A * 1000, ct); }
                finally { SendKey(vk, false); } // 即使被取消也释放，避免卡键
                break;
            }

            case AfkOpKind.Click:
                await ClickAsync(ins, left: true, ct);
                break;

            case AfkOpKind.RightClick:
                await ClickAsync(ins, left: false, ct);
                break;

            case AfkOpKind.MouseMove:
                SendMouseMove(ins.A, ins.B);
                await Task.Delay(10, ct);
                break;

            case AfkOpKind.Scroll:
                SendWheel(ins.A);
                await Task.Delay(10, ct);
                break;

            case AfkOpKind.TypeText:
            {
                var text = AfkWorkflowToken.DecodeText(ins.Text);
                if (text is not null)
                {
                    foreach (var ch in text)
                    {
                        ct.ThrowIfCancellationRequested();
                        SendUnicode(ch);
                        await Task.Delay(30, ct);
                    }
                }
                break;
            }

            case AfkOpKind.NamedKey:
            {
                var vk = AfkWorkflowToken.ResolveVk(ins);
                if (vk != 0)
                {
                    SendKey(vk, true);
                    await Task.Delay(40, ct);
                    SendKey(vk, false);
                }
                break;
            }

            case AfkOpKind.KeyDown:
            {
                var vk = AfkWorkflowToken.ResolveVk(ins);
                if (vk != 0) { SendKey(vk, true); held.Add(vk); }
                break;
            }

            case AfkOpKind.KeyUp:
            {
                var vk = AfkWorkflowToken.ResolveVk(ins);
                if (vk != 0) { SendKey(vk, false); held.Remove(vk); }
                break;
            }

            case AfkOpKind.RandomDelay:
            {
                var ms = ins.A <= 0 ? 0 : Random.Shared.Next(0, ins.A * 1000);
                await Task.Delay(ms, ct);
                break;
            }

            default:
                break;
        }
    }

    private static ushort GetVk(AfkInstruction ins) => ins.Kind switch
    {
        AfkOpKind.FunctionKey => (ushort)(0x70 + (ins.A - 1)), // VK_F1..VK_F24
        AfkOpKind.KeyCode => (ushort)ins.A,
        _ => 0
    };

    private static async Task ClickAsync(AfkInstruction ins, bool left, CancellationToken ct)
    {
        const int holdMs = 20;
        for (var c = 0; c < ins.A; c++)
        {
            ct.ThrowIfCancellationRequested();
            SendMouse(left, down: true);
            await Task.Delay(holdMs, ct);
            SendMouse(left, down: false);
            if (ins.B > holdMs)
                await Task.Delay(ins.B - holdMs, ct);
        }
    }

    private static void SendKey(ushort vk, bool down)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = down ? 0u : KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    // 左/右键的完整按下或抬起。原先只发 DOWN 且 SendMouse(left:false) 误发 RIGHTDOWN，
    // 导致连点变成「左键按下 + 右键按下」且永不释放——这是运行时挂机鼠标动作失效的根因（P0）。
    private static void SendMouse(bool left, bool down)
    {
        uint dwFlags = (left, down) switch
        {
            (true, true) => MOUSEEVENTF_LEFTDOWN,
            (true, false) => MOUSEEVENTF_LEFTUP,
            (false, true) => MOUSEEVENTF_RIGHTDOWN,
            (false, false) => MOUSEEVENTF_RIGHTUP
        };
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = 0,
                    dwFlags = dwFlags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static void SendMouseMove(int dx, int dy)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    mouseData = 0,
                    dwFlags = MOUSEEVENTF_MOVE,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static void SendWheel(int delta)
    {
        // 滚轮 delta 必须放在 mouseData 的高字（HIWORD）中，且为带符号 16 位。
        var clamped = Math.Clamp(delta, -32768, 32767);
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = (uint)((short)clamped << 16),
                    dwFlags = MOUSEEVENTF_WHEEL,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    // 用 Unicode 方式逐字符发送，支持任意可输入字符（含中文/符号）。
    private static void SendUnicode(char ch)
    {
        var down = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = (ushort)ch,
                    dwFlags = KEYEVENTF_UNICODE,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        var up = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = (ushort)ch,
                    dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        SendInput(1, new[] { down }, Marshal.SizeOf<INPUT>());
        SendInput(1, new[] { up }, Marshal.SizeOf<INPUT>());
    }

    private static IntPtr ResolveTargetWindow(int? targetPid)
    {
        if (targetPid.HasValue)
        {
            try
            {
                var p = Process.GetProcessById(targetPid.Value);
                if (!p.HasExited && p.MainWindowHandle != IntPtr.Zero)
                    return p.MainWindowHandle;
            }
            catch
            {
                // 进程不存在：回落到前台窗口
            }
        }
        return GetForegroundWindow();
    }

    private static void BringToFront(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        var fore = GetForegroundWindow();
        if (fore == hwnd) return;

        var foreThread = GetWindowThreadProcessId(fore, out _);
        var targetThread = GetWindowThreadProcessId(hwnd, out _);
        if (foreThread != targetThread && foreThread != 0 && targetThread != 0)
        {
            AttachThreadInput(foreThread, targetThread, true);
            SetForegroundWindow(hwnd);
            AttachThreadInput(foreThread, targetThread, false);
        }
        else
        {
            SetForegroundWindow(hwnd);
        }
    }
}
