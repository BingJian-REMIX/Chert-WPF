using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;
using Chert.Core.MultiInstance;
using Chert.Core.Mvvm;
using Chert.Core.Statistics;
using Chert.App.Services;

namespace Chert.App.ViewModels;

/// <summary>单个运行实例的实时资源占用（taskmgr 式逐进程行）。</summary>
public class InstancePerf : ObservableObject
{
    public int Pid { get; set; }
    public string VersionId { get; set; } = "";
    public DateTime StartedUtc { get; set; }
    public double CpuPercent { get; set; }
    public double MemoryMb { get; set; }
    public string CpuText => $"{CpuPercent:F0}%";
    public string MemoryText => $"{MemoryMb:F0} MB";
}

/// <summary>
/// 性能/实例监控面板（bug2.txt #7）：改用系统接口（Process / PerformanceCounter）逐进程采样，
/// 实时刷新，呈现类似任务管理器的实例资源表格 + 系统级 CPU/内存摘要。
/// </summary>
public class PerfViewModel : ObservableObject, IDisposable
{
    private ObservableCollection<InstancePerf> _instances = new();
    private PlayStats _stats = new();
    private string _statusMessage = "";
    private double _systemCpu;
    private double _memAvail;
    private readonly Dictionary<int, (TimeSpan Cpu, DateTime Ts)> _cpuSamples = new();
    private readonly PerformanceCounter? _cpuCounter;
    private readonly PerformanceCounter? _memCounter;
    private readonly DispatcherTimer _timer;
    private readonly ObservableCollection<double> _cpuHistory = new();
    private readonly ObservableCollection<double> _memHistory = new();
    private const int HistoryCap = 60;

    /// <summary>已彻底释放。只有宿主真的丢弃这个 VM 才会置位，切页不算。</summary>
    private bool _disposed;

    public ObservableCollection<InstancePerf> Instances
    {
        get => _instances;
        set => SetField(ref _instances, value);
    }

    public PlayStats Stats
    {
        get => _stats;
        set => SetField(ref _stats, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public int ProcessorCount => Environment.ProcessorCount;

    /// <summary>
    /// 运行中实例数。**不能直接绑 <c>Instances.Count</c>**：
    /// <c>SampleCore</c> 每次都把 <c>Instances</c> <b>整体替换</b>成新
    /// <c>ObservableCollection</c>（换实例），而 <c>Count</c> 的 PropertyChanged
    /// 链在旧集合上 —— 界面永远停在初始的 0（用户实测「实例检测显示 0」）。
    /// </summary>
    public int InstanceCount => _instances.Count;

    public double SystemCpu
    {
        get => _systemCpu;
        set => SetField(ref _systemCpu, value);
    }

    public double MemoryAvailableMb
    {
        get => _memAvail;
        set => SetField(ref _memAvail, value);
    }

    public string CpuUsageText => $"{SystemCpu:F0}%";
    public string MemoryUsageText => $"{MemoryAvailableMb:F0} MB 可用";

    // bug2.txt #7：实时折线图历史（滚动缓冲）
    public ObservableCollection<double> CpuHistory => _cpuHistory;
    public ObservableCollection<double> MemHistory => _memHistory;

    public double MemTotalMb { get; private set; }
    public double MemUsedPercent => MemTotalMb > 0
        ? Math.Min(100, Math.Max(0, (MemTotalMb - MemoryAvailableMb) / MemTotalMb * 100)) : 0;
    public string MemUsedText => $"{MemUsedPercent:F0}%";

    public ICommand RefreshCommand { get; }

    public PerfViewModel()
    {
        RefreshCommand = new RelayCommand(_ => Sample());
        MemTotalMb = QueryTotalPhysicalMemoryMb();
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
            _cpuCounter.NextValue();
        }
        catch { _cpuCounter = null; }
        try
        {
            _memCounter = new PerformanceCounter("Memory", "Available MBytes");
            _memCounter.NextValue();
        }
        catch { _memCounter = null; }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _timer.Tick += (_, _) => Sample();
        _timer.Start();

        // 延后到首帧渲染后再做跨进程采样，避免阻塞页面打开（构造期先给个占位状态）
        StatusMessage = "正在采集性能数据…";
        if (System.Windows.Application.Current?.Dispatcher is { } disp)
            disp.BeginInvoke(DispatcherPriority.Loaded, (Action)Sample);
        else
            Sample();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private static double QueryTotalPhysicalMemoryMb()
    {
        try
        {
            var info = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            return GlobalMemoryStatusEx(ref info) ? info.ullTotalPhys / 1048576.0 : 0;
        }
        catch { return 0; }
    }

    private void Sample()
    {
        if (_disposed) return;
        try
        {
            SampleCore();
        }
        catch (Exception ex)
        {
            // 任何采样异常都不能冒泡到构造器 / Dispatcher，否则性能页直接加载失败。
            StatusMessage = "采样异常：" + ex.Message;
        }
    }

    private void SampleCore()
    {
        // 含跨进程扫描：与状态栏口径一致（用户反馈游戏在跑却显示「0 个实例」）
        var list = InstanceTracker.ListActiveIncludingExternal(Chert.Core.Utils.GameConstants.DefaultGameRoot);
        var now = DateTime.Now;
        var rows = new List<InstancePerf>();
        foreach (var inst in list)
        {
            double cpu = 0, mem = 0;
            try
            {
                using var p = Process.GetProcessById(inst.Pid);
                if (!p.HasExited)
                {
                    mem = p.WorkingSet64 / 1048576.0;
                    var cpuTime = p.TotalProcessorTime;
                    if (_cpuSamples.TryGetValue(inst.Pid, out var prev))
                    {
                        var dt = (now - prev.Ts).TotalSeconds;
                        if (dt > 0.01)
                            cpu = Math.Min(100, Math.Max(0,
                                (cpuTime.TotalSeconds - prev.Cpu.TotalSeconds) / dt / ProcessorCount * 100));
                    }
                    _cpuSamples[inst.Pid] = (cpuTime, now);
                }
            }
            catch { _cpuSamples.Remove(inst.Pid); }

            rows.Add(new InstancePerf
            {
                Pid = inst.Pid,
                VersionId = inst.VersionId,
                StartedUtc = inst.StartedUtc,
                CpuPercent = cpu,
                MemoryMb = mem
            });
        }

        // 清理已退出进程的采样基线
        foreach (var pid in _cpuSamples.Keys.ToList())
            if (!list.Any(i => i.Pid == pid)) _cpuSamples.Remove(pid);

        Instances = new ObservableCollection<InstancePerf>(rows);
        // ★ 集合已整体替换，派生属性必须显式通知，否则「实例数」卡片永远显示 0。
        OnPropertyChanged(nameof(InstanceCount));

        try { if (_cpuCounter != null) SystemCpu = _cpuCounter.NextValue(); } catch { }
        try { if (_memCounter != null) MemoryAvailableMb = _memCounter.NextValue(); } catch { }
        // P06：CpuUsageText / MemoryUsageText 是计算属性（分别依赖 SystemCpu / MemoryAvailableMb），
        // 仅通知底层字段不会刷新这两个派生 TextBlock；补上显式通知，让大数字随采样实时刷新。
        OnPropertyChanged(nameof(CpuUsageText));
        OnPropertyChanged(nameof(MemoryUsageText));

        // bug2.txt #7：滚动历史缓冲，供实时折线图使用
        _cpuHistory.Add(SystemCpu);
        if (_cpuHistory.Count > HistoryCap) _cpuHistory.RemoveAt(0);
        _memHistory.Add(MemUsedPercent);
        if (_memHistory.Count > HistoryCap) _memHistory.RemoveAt(0);
        OnPropertyChanged(nameof(MemUsedPercent));
        OnPropertyChanged(nameof(MemUsedText));

        Stats = PlaytimeTracker.Load(LauncherService.Instance.GameRoot);
        StatusMessage = list.Count > 0
            ? $"当前运行 {list.Count} 个游戏实例"
            : "没有正在运行的游戏实例";
    }

    /// <summary>
    /// 开始定时采样。页面进入视野时调用。
    /// <para>
    /// 工具箱的面板视图是<b>缓存</b>的（<c>ToolboxPanelItem.View</c> 只 new 一次），
    /// 所以切回性能页走的是同一个 VM —— 采样必须能重新开起来，不能只靠构造器那一次。
    /// </para>
    /// </summary>
    public void Start()
    {
        if (_disposed) return;
        _timer.Start();
        // 立刻补一帧：否则切回来先看到的是上一次离开时的过期数据
        Sample();
    }

    /// <summary>
    /// 停止定时采样。页面离开视野时调用 —— 只停表，<b>不释放计数器</b>：
    /// 视图还缓存在那儿，切回来要接着用；真要拆才走 <see cref="Dispose"/>。
    /// </summary>
    public void Stop()
    {
        if (_disposed) return;
        _timer.Stop();
    }

    /// <summary>彻底释放（宿主丢弃此 VM 时）。幂等 —— 面板视图被缓存，别指望它只跑一次。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _cpuCounter?.Dispose();
        _memCounter?.Dispose();
    }
}
