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
        var list = InstanceTracker.ListActive();
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

        try { if (_cpuCounter != null) SystemCpu = _cpuCounter.NextValue(); } catch { }
        try { if (_memCounter != null) MemoryAvailableMb = _memCounter.NextValue(); } catch { }

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

    public void Dispose()
    {
        _timer.Stop();
        _cpuCounter?.Dispose();
        _memCounter?.Dispose();
    }
}
