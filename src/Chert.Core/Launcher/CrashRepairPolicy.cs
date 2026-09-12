namespace Chert.Core.Launcher;

/// <summary>
/// 崩溃自动修复的触发策略，用户可在设置中配置。
/// </summary>
public enum CrashRepairPolicy
{
    /// <summary>始终尝试自动修复：崩溃后不经提示直接尝试修复并重启，循环直至成功或无法修复。</summary>
    Always,

    /// <summary>每次询问：崩溃后展示分析报告与"尝试自动修复"按钮，由用户确认后再修复。</summary>
    Ask,

    /// <summary>始终拒绝：仅展示崩溃详情与手动建议，从不自动修复。</summary>
    Never
}
