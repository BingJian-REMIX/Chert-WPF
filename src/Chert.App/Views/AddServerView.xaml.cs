using System.Windows;
using System.Windows.Controls;
using Chert.App.ViewModels;

namespace Chert.App.Views;

public partial class AddServerView : UserControl
{
    public AddServerViewModel VM { get; }

    public AddServerView(string? existingName = null, string? existingAddress = null)
    {
        VM = new AddServerViewModel(existingName, existingAddress);
        DataContext = VM;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ((Button)CancelButton).Click += (_, _) => Window.GetWindow(this)?.Close();
            ((Button)OkButton).Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(VM.Name)) { VM.Error = "名称不能为空"; return; }
                if (string.IsNullOrWhiteSpace(VM.Address)) { VM.Error = "地址不能为空（如 example.com:25565）"; return; }
                VM.Confirmed = true;
                Window.GetWindow(this)?.Close();
            };
        };
    }
}
