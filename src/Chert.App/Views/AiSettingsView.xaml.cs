using System.Windows;
using System.Windows.Controls;
using Chert.App.ViewModels;

namespace Chert.App.Views;

public partial class AiSettingsView : UserControl
{
    private AiSettingsViewModel Vm => (AiSettingsViewModel)DataContext;

    public AiSettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (Vm is not null)
                AiKeyPw.Password = Vm.AiApiKey;
        };
    }

    // AI 助手：本地模型切换（未下载时弹确认窗，取消则回退）
    private async void LocalModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LocalModelCombo.SelectedValue is string name && Vm is not null)
            await Vm.TrySelectLocalModelAsync(name);
    }

    // AI 助手：API Key 密文实时同步到配置
    private void AiKeyPw_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) Vm.AiApiKey = AiKeyPw.Password;
    }
}
