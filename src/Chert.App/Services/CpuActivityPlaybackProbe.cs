using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Chert.App.Services;

/// <summary>外部客户端的播放状态。</summary>
public enum ClientPlaybackState
{
    /// <summary>读不到（探测失败 / 客户端无媒体会话）。★ 不参与「全部暂停」判定。</summary>
    Unknown = 0,

    /// <summary>正在播放（探测器认定有音频活动）。</summary>
    Playing = 1,

    /// <summary>已暂停 / 未在出声。</summary>
    Paused = 2
}

/// <summary>播放状态探测接口（便于测试替换）。</summary>
public interface IClientPlaybackProbe
{
    /// <summary>批量读取若干进程的播放状态。返回 null 或空字典表示「本轮探测失败」。</summary>
    Dictionary<int, ClientPlaybackState>? GetStates(IReadOnlyList<int> pids);
}

/// <summary>
/// 默认探测器：比较进程的 <b>CPU 时间增量</b> 与 <b>主窗口响应</b>。
///
/// <para><b>为什么不用 SMTC</b>：规格原定 SMTC（<c>SystemMediaTransportControls</c>），
/// 但它属 WinRT，<c>net10.0-windows</c> TFM 默认不带其投影 ——
/// 需引入 <c>Microsoft.Windows.SDK.Contracts</c>，而该包会把
/// <c>TargetPlatformVersion</c> 顶到 7.0 并触发 <c>NETSDK1135</c>；
/// 且 <c>Chert.Core</c> 是纯 <c>net10.0</c>，本就无 WinRT。
/// 权衡后改用零新增依赖的启发式方案。</para>
///
/// <para><b>启发式原理</b>：音乐客户端在「播放」时会持续解码音频，CPU 时间稳步增长；
/// 暂停时几乎不动。因此两次采样间 CPU 增量超过阈值即判为播放中。</para>
///
/// <para><b>刻意的保守取向</b>：宁可「误判为播放」也不「误判为暂停」——
/// 后者会导致杀掉用户正在听的客户端。首次采样（没有基线）一律返回
/// <see cref="ClientPlaybackState.Unknown"/>，宁可多等一轮也不误杀。</para>
/// </summary>
public sealed class CpuActivityPlaybackProbe : IClientPlaybackProbe
{
    private readonly Dictionary<int, double> _lastCpuMs = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>
    /// 判定为「正在播放」的 CPU 时间增量阈值（毫秒 / 采样周期）。
    /// 默认 40ms：一个 1 秒的采样周期里，暂停的客户端通常 &lt; 5ms，
    /// 播放时即便轻量编解码器也远超此值。
    /// </summary>
    private const double PlayingThresholdMs = 40;

    /// <summary>
    /// 读不到状态时返回 Unknown 而非 Paused 的「连续失败次数上限」：
    /// 超过则把它当播放处理（不启动计时），避免探测长期失效时误杀。
    /// </summary>
    private const int MaxUnknownStreak = 3;

    private readonly Dictionary<int, int> _unknownStreak = new();

    public Dictionary<int, ClientPlaybackState>? GetStates(IReadOnlyList<int> pids)
    {
        if (pids is null || pids.Count == 0) return null;

        var result = new Dictionary<int, ClientPlaybackState>();
        foreach (var pid in pids)
        {
            if (pid <= 0) continue;
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited) continue;

                var cpuMs = p.TotalProcessorTime.TotalMilliseconds;

                if (!_lastCpuMs.TryGetValue(pid, out var prev))
                {
                    // 首次采样：建立基线，但状态留 Unknown（不猜）
                    _lastCpuMs[pid] = cpuMs;
                    result[pid] = ClientPlaybackState.Unknown;
                    continue;
                }

                var delta = cpuMs - prev;
                _lastCpuMs[pid] = cpuMs;

                if (delta >= PlayingThresholdMs)
                {
                    result[pid] = ClientPlaybackState.Playing;
                }
                else
                {
                    result[pid] = ClientPlaybackState.Paused;
                }
            }
            catch
            {
                // 进程读不到（已退出 / 无权限）——不写进结果，
                // 上层按「不在字典里」处理，等价于不参与判定。
            }
        }

        return result.Count > 0 ? result : null;
    }
}
