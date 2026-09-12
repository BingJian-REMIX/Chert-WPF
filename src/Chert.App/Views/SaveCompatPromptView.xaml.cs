using System.Windows;
using Chert.App.ViewModels;

namespace Chert.App.Views;

public partial class SaveCompatPromptView : Window
{
    public SaveCompatPromptView()
    {
        InitializeComponent();
    }

    public SaveCompatPromptView(string gameRoot, string versionId,
        System.Collections.Generic.IEnumerable<Chert.Core.Save.SaveCompatibilityReport> incompatible) : this()
    {
        var vm = new SaveCompatPromptViewModel(gameRoot, versionId, incompatible);
        vm.Decision += proceed =>
        {
            Proceed = proceed;
            Dispatcher.Invoke(Close);
        };
        DataContext = vm;
    }

    /// <summary>用户的最终决策：true=继续启动，false=取消。</summary>
    public bool Proceed { get; private set; }
}
