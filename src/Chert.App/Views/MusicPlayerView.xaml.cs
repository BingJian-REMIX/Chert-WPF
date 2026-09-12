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
        // bug #10：点击 / 拖动进度条松手时跳转（拖动过程中不触发，避免抖动）
        SeekBar.PreviewMouseUp += (_, _) => MusicPlayerViewModel.Instance.SeekCommand.Execute(SeekBar.Value);
    }
}
