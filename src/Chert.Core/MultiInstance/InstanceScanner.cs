using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Chert.Core.MultiInstance;

/// <summary>
/// 跨进程发现正在运行的游戏实例（补 <see cref="InstanceTracker"/> 的盲区）。
///
/// <para><b>为什么需要</b>：<see cref="InstanceTracker"/> 是**进程内静态字典**，只能记录
/// 「本次启动器进程亲自拉起」的游戏。以下场景会漏统计，而用户看到的就是「运行 0 个实例」：
/// ① 启动器重启过（游戏仍在后台跑，字典已随进程清空）；
/// ② 游戏由外部 / 旧版本启动器拉起；
/// ③ 启动器崩溃重启后接手。</para>
///
/// <para><b>判定依据</b>：进程映像为 <c>javaw.exe</c>（MC 客户端的 Java 启动器），
/// 且其**命令行包含本启动器当前游戏目录**。后者是关键 —— 同机可能跑着别的启动器的 MC，
/// 只看进程名会误统计。用 <c>--gameDir</c> 参数（若有）或工作目录路径做包含匹配。</para>
///
/// <para><b>性能</b>：WMI 查询较慢（约 100–300ms），故只在
/// <see cref="Scan"/> 被调用时执行一次，调用方自行控制频率（状态栏 / 性能页各自按需）。</para>
/// </summary>
public static class InstanceScanner
{
    /// <summary>MC 客户端常见的 Java 进程名（小写 javaw 是带控制台的窗口版）。</summary>
    private static readonly string[] JavaImageNames =
    {
        "javaw.exe", "java.exe"
    };

    /// <summary>
    /// 扫描当前由本启动器游戏目录启动、且仍存活的游戏进程。
    /// <paramref name="gameRoot"/> 为当前生效的游戏目录（不区分大小写匹配）。
    /// </summary>
    public static List<RunningInstance> Scan(string? gameRoot)
    {
        var result = new List<RunningInstance>();
        if (string.IsNullOrWhiteSpace(gameRoot)) return result;

        var root = gameRoot.Trim().TrimEnd('\\', '/');
        if (root.Length == 0) return result;

        try
        {
            foreach (var p in EnumerateJavaProcesses())
            {
                var cmd = ReadCommandLine(p.Id);
                if (cmd is null) continue;

                // 命令行必须明确指向本启动器的游戏目录，避免统计到别的启动器的 MC
                if (!MentionsGameRoot(cmd, root)) continue;

                result.Add(new RunningInstance
                {
                    Pid = p.Id,
                    VersionId = ExtractVersionId(cmd),
                    StartedUtc = SafeGetStartTime(p.Id),
                    IsAlive = true
                });
            }
        }
        catch
        {
            // WMI 不可用（精简版 Windows / 权限不足）时静默返回空列表：
            // 界面显示 0 个实例，但不影响启动、性能采样等其它功能。
        }

        return result;
    }

    /// <summary>枚举本机所有 java / javaw 进程。</summary>
    private static List<Process> EnumerateJavaProcesses()
    {
        var list = new List<Process>();
        foreach (var name in JavaImageNames)
        {
            try
            {
                list.AddRange(Process.GetProcessesByName(
                    Path.GetFileNameWithoutExtension(name)));
            }
            catch
            {
                // 枚举某个名字失败不影响另一个
            }
        }
        return list;
    }

    /// <summary>
    /// 读取进程命令行。用 <c>NtQueryInformationProcess</c> + 读 PEB，
    /// 避免引入 WMI（更快、无需 <c>System.Management</c> 程序集引用）。
    /// </summary>
    private static string? ReadCommandLine(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            return QueryCommandLine(proc.Handle);
        }
        catch
        {
            return null;
        }
    }

    private const int ProcessBasicInformation = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr Reserved3;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PEB
    {
        public IntPtr Reserved1;
        public IntPtr Reserved2;
        public IntPtr Reserved3;
        public IntPtr LpCommandLine;   // UNICODE_STRING
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr hProcess, int ProcessInformationClass,
        ref PROCESS_BASIC_INFORMATION pProcessInformation,
        int ProcessInformationLength, out int ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer,
        int nSize, out int lpNumberOfBytesRead);

    private static string? QueryCommandLine(IntPtr handle)
    {
        var pbi = new PROCESS_BASIC_INFORMATION();
        if (NtQueryInformationProcess(handle, ProcessBasicInformation, ref pbi,
                Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out _) != 0)
            return null;
        if (pbi.PebBaseAddress == IntPtr.Zero) return null;

        // PEB.ProcessParameters 在 PEB+0x20（x64）/ +0x10（x86）
        var pebBytes = new byte[IntPtr.Size == 8 ? 0x28 : 0x14];
        if (!ReadProcessMemory(handle, pbi.PebBaseAddress, pebBytes, pebBytes.Length, out _))
            return null;

        var procParamsOffset = IntPtr.Size == 8 ? 0x20 : 0x10;
        var paramsAddr = new IntPtr(BitConverter.ToInt64(pebBytes, procParamsOffset));

        // RTL_USER_PROCESS_PARAMETERS.CommandLine（UNICODE_STRING）在 +0x70（x64）/ +0x40（x86）
        var usOffset = IntPtr.Size == 8 ? 0x70 : 0x40;
        var usBytes = new byte[Marshal.SizeOf<UNICODE_STRING>()];
        if (!ReadProcessMemory(handle, new IntPtr(paramsAddr.ToInt64() + usOffset), usBytes, usBytes.Length, out _))
            return null;

        // UNICODE_STRING: { ushort Length; ushort MaximumLength; IntPtr Buffer; }
        var len = BitConverter.ToUInt16(usBytes, 0);
        var buf = new IntPtr(BitConverter.ToInt64(usBytes, 4));
        if (len == 0 || buf == IntPtr.Zero) return null;

        var text = new byte[len];
        if (!ReadProcessMemory(handle, buf, text, text.Length, out _)) return null;
        return Encoding.Unicode.GetString(text);
    }

    /// <summary>命令行里是否提到了该游戏目录（不区分大小写，含路径分隔符容错）。</summary>
    private static bool MentionsGameRoot(string cmd, string root)
    {
        if (cmd.Contains(root, StringComparison.OrdinalIgnoreCase)) return true;

        // 命令行里常见的是不带引号且分隔符可能不同（\ 与 /），做一次宽松匹配：
        // 取目录名的最后一段，判断是否作为独立片段出现。
        var leaf = root.Split('\\', '/').LastOrDefault();
        if (string.IsNullOrEmpty(leaf)) return false;
        return cmd.Contains(leaf, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>从命令行里抠出版本 id（取 <c>--version</c> 后的值，或 jar 路径的父目录名）。</summary>
    private static string ExtractVersionId(string cmd)
    {
        var parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals("--version", StringComparison.OrdinalIgnoreCase))
                return parts[i + 1].Trim('"');
        }

        // 退而求其次：找形如 .../versions/<id>/<id>.jar 的路径
        foreach (var p in parts)
        {
            var s = p.Trim('"');
            if (!s.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) continue;
            var dir = Path.GetDirectoryName(s);
            if (string.IsNullOrEmpty(dir)) continue;
            var leaf = new DirectoryInfo(dir).Name;
            // 命中 versions/<id> 或隔离目录里的 <id> 目录
            if (leaf.Length > 0 && !leaf.Equals("versions", StringComparison.OrdinalIgnoreCase))
                return leaf;
        }
        return "未知版本";
    }

    private static DateTime SafeGetStartTime(int pid)
    {
        try { return Process.GetProcessById(pid).StartTime.ToUniversalTime(); }
        catch { return DateTime.UtcNow; }
    }
}
