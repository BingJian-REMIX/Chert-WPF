namespace Chert.App.Themes;

/// <summary>
/// 图标高分辨率（2x）开关。开启后 <see cref="IconImage"/> 优先加载 <c>@2x</c> 资源，
/// 在高 DPI（如 4K / 缩放 &gt; 100%）屏幕上渲染更清晰。
/// 状态变化时广播 <see cref="HighDpiChanged"/>，所有 <see cref="PngIcon"/> 控件据此重新加载图标。
/// <para>状态由设置页“适配高分辨率屏幕”开关驱动，并持久化到启动器配置。</para>
/// </summary>
public static class IconManager
{
    private static bool _highDpi;

    /// <summary>是否启用 2x 高清图标（高 DPI 适配）。</summary>
    public static bool HighDpi
    {
        get => _highDpi;
        set
        {
            if (_highDpi == value) return;
            _highDpi = value;
            HighDpiChanged?.Invoke();
        }
    }

    /// <summary>高清图标开关变化事件（PngIcon 订阅以重载图标）。</summary>
    public static event Action? HighDpiChanged;
}
