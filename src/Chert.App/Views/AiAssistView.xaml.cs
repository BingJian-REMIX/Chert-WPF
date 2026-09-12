using System.Windows;
using System.Windows.Controls;
using Chert.App.ViewModels;

namespace Chert.App.Views;

public partial class AiAssistView : UserControl
{
    private AiAssistViewModel Vm => (AiAssistViewModel)DataContext;

    public AiAssistView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Vm.Messages.CollectionChanged += (_, _) => ScrollToBottom();
        Vm.PropertyChanged += (_, ev) =>
        {
            if (ev.PropertyName == nameof(AiAssistViewModel.IsBusy) ||
                ev.PropertyName == nameof(AiAssistViewModel.ShowWelcome))
                ScrollToBottom();
        };
        UpdateHint();
    }

    private void ScrollToBottom() => ChatScroll?.ScrollToEnd();

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateHint();
    private void InputBox_GotFocus(object sender, RoutedEventArgs e) => InputHint.Visibility = Visibility.Collapsed;
    private void InputBox_LostFocus(object sender, RoutedEventArgs e) => UpdateHint();

    private void UpdateHint()
    {
        InputHint.Visibility = string.IsNullOrEmpty(InputBox.Text) && !InputBox.IsFocused
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
