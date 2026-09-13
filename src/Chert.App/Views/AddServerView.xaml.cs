using System.Windows;
using System.Windows.Controls;
using Chert.App.ViewModels;
using Chert.Core.Localization;

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
                if (string.IsNullOrWhiteSpace(VM.Name)) { VM.Error = LocaleManager.T("server.name_empty"); return; }
                if (string.IsNullOrWhiteSpace(VM.Address)) { VM.Error = LocaleManager.T("server.addr_empty"); return; }
                VM.Confirmed = true;
                Window.GetWindow(this)?.Close();
            };
        };
    }
}
