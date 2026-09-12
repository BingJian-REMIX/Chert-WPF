using Chert.Core.Mvvm;

namespace Chert.App.ViewModels;

public class MainViewModel : ObservableObject
{
    private string _title = "MCLCS — Minecraft 启动器";
    private int _selectedTabIndex;

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetField(ref _selectedTabIndex, value);
    }
}
