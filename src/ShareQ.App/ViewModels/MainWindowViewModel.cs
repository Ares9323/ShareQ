using CommunityToolkit.Mvvm.ComponentModel;

namespace ShareQ.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "ShareQ";
}
