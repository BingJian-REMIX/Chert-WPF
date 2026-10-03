using Chert.Core.Music;
using Chert.Core.Profiles;
using Chert.Core.Utils;

namespace Chert.App.Services;

/// <summary>
/// <see cref="ClientLifecycleManager"/> 的进程级单例（规格 · 实现项 2）。
///
/// <para>此前 <see cref="ClientLifecycleManager"/> 只有类、没有实例 —— 判定与计时逻辑
/// 写得完整却从未被启动过，等于整条「宽限期后自动结束客户端」链路是死的。
/// 这里补上唯一的装配点，并把它挂到 <see cref="MusicModeManager"/> 的模式切换上。</para>
///
/// <para>为什么不用 DI 容器：项目里其它服务（LauncherService / UIService）都是手写单例，
/// 保持一致，不为一个类引入容器。</para>
/// </summary>
public sealed class ClientLifecycleService : IDisposable
{
    private static ClientLifecycleService? _instance;

    /// <summary>进程级单例。<c>EnsureCreated</c> 是幂等的。</summary>
    public static ClientLifecycleService Instance =>
        _instance ??= new ClientLifecycleService();

    /// <summary>是否已创建（测试可在清理时调用 <see cref="Dispose"/> 后再复位）。</summary>
    public static bool Exists => _instance is not null;

    private readonly ClientLifecycleManager _manager;

    private ClientLifecycleService()
    {
        _manager = new ClientLifecycleManager(
            new CpuActivityPlaybackProbe(),
            () => ReadPrefs(),
            () => MusicModeManager.Current);

        // 模式切换时把宽限期计时清零：切走本地客户端模式说明用户换了音源，
        // 但是否重新计时由 Tick 依据「全部暂停」自行判断，这里只做事件转发。
        MusicModeManager.ModeChanged += OnModeChanged;
    }

    /// <summary>底层管理器（供播放器登记进程）。</summary>
    public ClientLifecycleManager Manager => _manager;

    /// <summary>剩余宽限期秒数（-1 = 不在计时）。</summary>
    public int RemainingGraceSeconds => _manager.RemainingGraceSeconds;

    /// <summary>当前跟踪的客户端数。</summary>
    public int TrackedCount => _manager.TrackedCount;

    /// <summary>启动轮询（幂等）。</summary>
    public void Start() => _manager.Start();

    /// <summary>登记一个由启动器拉起的客户端进程。</summary>
    public static void Register(int pid, string? name = null) =>
        Instance._manager.Register(pid, name);

    /// <summary>状态变化（剩余秒数 / 客户端数）—— 供 UI 提示。</summary>
    public event Action<int, int>? GracefulCloseChanged
    {
        add => _manager.GracefulCloseChanged += value;
        remove => _manager.GracefulCloseChanged -= value;
    }

    private void OnModeChanged(MusicSourceMode mode)
    {
        // 进入本地客户端模式时确保轮询在跑（首次使用客户端功能才有实例）
        if (mode == MusicSourceMode.LocalClient) Start();
    }

    /// <summary>读取本地客户端模式设置（容错归一，非法值已由 Normalized 处理）。</summary>
    private static MusicClientPrefs ReadPrefs()
    {
        try
        {
            return ProfileStore.Load(GameConstants.DefaultGameRoot).MusicClient.Normalized();
        }
        catch
        {
            return new MusicClientPrefs();
        }
    }

    public void Dispose()
    {
        MusicModeManager.ModeChanged -= OnModeChanged;
        _manager.Dispose();
        if (ReferenceEquals(_instance, this)) _instance = null;
    }
}
