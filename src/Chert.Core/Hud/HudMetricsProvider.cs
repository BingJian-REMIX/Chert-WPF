using System.Diagnostics;
using System.Text;

namespace Chert.Core.Hud;

/// <summary>一次采样得到的 HUD 指标。</summary>
public class HudMetrics
{
    public double Fps { get; set; }

    /// <summary>游戏进程已用内存（MB）。</summary>
    public double MemoryUsedMb { get; set; }

    /// <summary>分配给游戏的最大内存（MB）。</summary>
    public double MemoryMaxMb { get; set; }

    public double CpuPercent { get; set; }

    /// <summary>到服务器的延迟（毫秒），-1 表示单人 / 未知。</summary>
    public int PingMs { get; set; } = -1;

    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public string? Biome { get; set; }

    /// <summary>游戏内时间（0-23999 tick）。</summary>
    public long GameTicks { get; set; } = -1;

    /// <summary>本次会话已进行时长。</summary>
    public TimeSpan SessionTime { get; set; }

    public double MemoryPercent => MemoryMaxMb <= 0 ? 0 : MemoryUsedMb / MemoryMaxMb * 100;

    /// <summary>游戏内时间的 HH:mm 表示。</summary>
    public string GameClock
    {
        get
        {
            if (GameTicks < 0) return "--:--";
            var t = (GameTicks + 6000) % 24000;        // tick 0 = 早上 6 点
            return $"{t / 1000:00}:{t % 1000 * 60 / 1000:00}";
        }
    }
}

/// <summary>
/// HUD 指标采集与渲染。
/// <para>进程级指标（内存 / CPU）直接从 <see cref="Process"/> 读取；
/// FPS、坐标、生物群系等游戏内数据由配套 Mod 或日志解析回填，
/// 采不到时以 "--" 呈现，不影响其余字段。</para>
/// </summary>
public class HudMetricsProvider
{
    private DateTime _lastSampleTime = DateTime.MinValue;
    private TimeSpan _lastCpuTime = TimeSpan.Zero;

    /// <summary>会话开始时间（用于 SessionTime 字段）。</summary>
    public DateTime SessionStart { get; set; } = DateTime.Now;

    /// <summary>由外部（Mod / 日志解析）回填的游戏内数据。</summary>
    public HudMetrics External { get; } = new();

    /// <summary>
    /// 采样一次。<paramref name="gameProcess"/> 为 null 或已退出时，进程相关字段留 0。
    /// </summary>
    public HudMetrics Sample(Process? gameProcess, double maxMemoryMb = 0)
    {
        var m = new HudMetrics
        {
            Fps = External.Fps,
            PingMs = External.PingMs,
            X = External.X,
            Y = External.Y,
            Z = External.Z,
            Biome = External.Biome,
            GameTicks = External.GameTicks,
            MemoryMaxMb = maxMemoryMb,
            SessionTime = DateTime.Now - SessionStart
        };

        try
        {
            if (gameProcess is { HasExited: false })
            {
                gameProcess.Refresh();
                m.MemoryUsedMb = Math.Round(gameProcess.WorkingSet64 / 1024.0 / 1024, 1);
                m.CpuPercent = Math.Round(SampleCpu(gameProcess), 1);
            }
        }
        catch
        {
            // 进程已退出 / 权限不足：保持 0
        }

        return m;
    }

    /// <summary>计算两次采样之间的 CPU 占用百分比（跨核归一化）。</summary>
    private double SampleCpu(Process p)
    {
        var now = DateTime.UtcNow;
        var cpu = p.TotalProcessorTime;

        if (_lastSampleTime == DateTime.MinValue)
        {
            _lastSampleTime = now;
            _lastCpuTime = cpu;
            return 0;
        }

        var wallMs = (now - _lastSampleTime).TotalMilliseconds;
        var cpuMs = (cpu - _lastCpuTime).TotalMilliseconds;
        _lastSampleTime = now;
        _lastCpuTime = cpu;

        if (wallMs <= 0) return 0;
        var pct = cpuMs / (wallMs * Environment.ProcessorCount) * 100;
        return Math.Clamp(pct, 0, 100);
    }

    /// <summary>按配置把指标渲染成多行文本（每行一个字段）。</summary>
    public static string Render(HudMetrics m, HudConfig cfg)
    {
        var sb = new StringBuilder();

        if (cfg.Has(HudField.Fps))
            sb.AppendLine($"FPS  {(m.Fps > 0 ? m.Fps.ToString("F0") : "--")}");

        if (cfg.Has(HudField.Memory))
            sb.AppendLine(m.MemoryMaxMb > 0
                ? $"内存  {m.MemoryUsedMb:F0} / {m.MemoryMaxMb:F0} MB ({m.MemoryPercent:F0}%)"
                : $"内存  {m.MemoryUsedMb:F0} MB");

        if (cfg.Has(HudField.Cpu))
            sb.AppendLine($"CPU  {m.CpuPercent:F0}%");

        if (cfg.Has(HudField.Ping))
            sb.AppendLine($"延迟  {(m.PingMs >= 0 ? m.PingMs + " ms" : "--")}");

        if (cfg.Has(HudField.Coordinates))
            sb.AppendLine($"坐标  {m.X:F1} / {m.Y:F1} / {m.Z:F1}");

        if (cfg.Has(HudField.Biome))
            sb.AppendLine($"群系  {(string.IsNullOrWhiteSpace(m.Biome) ? "--" : m.Biome)}");

        if (cfg.Has(HudField.GameTime))
            sb.AppendLine($"时间  {m.GameClock}");

        if (cfg.Has(HudField.SessionTime))
            sb.AppendLine($"时长  {m.SessionTime:hh\\:mm\\:ss}");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 从游戏日志行里解析 HUD 相关数据（配套 Mod 会输出 <c>[MCLCS-HUD] key=value</c> 行）。
    /// 解析成功返回 true 并回填 <see cref="External"/>。
    /// </summary>
    public bool TryConsumeLogLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        var idx = line.IndexOf("[MCLCS-HUD]", StringComparison.Ordinal);
        if (idx < 0) return false;

        var payload = line[(idx + "[MCLCS-HUD]".Length)..].Trim();
        var any = false;

        foreach (var pair in payload.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            var key = pair[..eq].Trim().ToLowerInvariant();
            var val = pair[(eq + 1)..].Trim();

            switch (key)
            {
                case "fps" when double.TryParse(val, out var fps): External.Fps = fps; any = true; break;
                case "ping" when int.TryParse(val, out var ping): External.PingMs = ping; any = true; break;
                case "x" when double.TryParse(val, out var x): External.X = x; any = true; break;
                case "y" when double.TryParse(val, out var y): External.Y = y; any = true; break;
                case "z" when double.TryParse(val, out var z): External.Z = z; any = true; break;
                case "biome": External.Biome = val; any = true; break;
                case "ticks" when long.TryParse(val, out var t): External.GameTicks = t; any = true; break;
            }
        }
        return any;
    }
}
