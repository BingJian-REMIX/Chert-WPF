using System.Windows;
using Chert.App.Views;

namespace Chert.App.Views;

/// <summary>
/// 版本设置的宿主窗口（对齐 MCLCS-Linux 的 VersionSettingsDialog）。
/// 以模态方式打开，内部承载 <see cref="VersionSettingsView"/>。
/// </summary>
public class VersionSettingsWindow : Window
{
    private VersionSettingsWindow(string gameRoot, string versionId, string versionType, Action? onVersionsChanged)
    {
        var view = new VersionSettingsView(gameRoot, versionId, versionType);
        if (onVersionsChanged is not null)
            view.VM.VersionsChanged += onVersionsChanged;

        Title = $"版本设置 · {versionId}";
        Content = view;
        Width = 760;
        Height = 720;
        MinWidth = 640;
        MinHeight = 520;
        Owner = Application.Current.MainWindow;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.Transparent;
    }

    /// <summary>以模态方式打开版本设置；<paramref name="onVersionsChanged"/> 在安装加载器导致版本新增后回调。</summary>
    public static void Open(string gameRoot, string versionId, string versionType, Action? onVersionsChanged = null)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            new VersionSettingsWindow(gameRoot, versionId, versionType, onVersionsChanged).ShowDialog();
        });
    }
}
