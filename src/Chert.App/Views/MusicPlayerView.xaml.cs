using System.Windows.Controls;
using System.Windows.Input;
using Chert.App.ViewModels;

namespace Chert.App.Views;

/// <summary>音乐播放器面板（工具箱）。播放状态与命令来自 <see cref="MusicPlayerViewModel"/> 单例；
/// 实际解码由主窗口注入的 MediaElement 宿主完成，本面板只负责展示与交互。</summary>
public partial class MusicPlayerView : UserControl
{
    public MusicPlayerView()
    {
        InitializeComponent();
        DataContext = MusicPlayerViewModel.Instance;
        // bug #10 + P13：点击 / 拖动进度条跳转。统一走 SeekInteraction —— 完全接管鼠标时序，
        // 由坐标算值并在松手时提交；拖拽期间置 IsSeeking，暂停定时器回写位置，避免拖柄回弹。
        SeekInteraction.Attach(
            SeekBar,
            ratio =>
            {
                if (MusicPlayerViewModel.Instance.HasProgress)
                    MusicPlayerViewModel.Instance.SeekCommand.Execute(ratio);
            },
            () => MusicPlayerViewModel.Instance.IsSeeking = true,
            () => MusicPlayerViewModel.Instance.IsSeeking = false);
    }
}
