using System.Windows.Controls;
using System.Windows.Input;
using Chert.App.ViewModels;

namespace Chert.App.Views;

public partial class GameView : UserControl
{
    public GameView()
    {
        InitializeComponent();
        DataContext = new GameViewModel();
    }

    private void AnnualReportCard_Click(object sender, MouseButtonEventArgs e)
    {
        ((GameViewModel)DataContext).OpenAnnualReportCommand.Execute(null);
    }
}
