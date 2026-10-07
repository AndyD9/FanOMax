using CommunityToolkit.Mvvm.ComponentModel;

namespace FanOMax.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Greeting { get; set; } = "FanOMax";
}
