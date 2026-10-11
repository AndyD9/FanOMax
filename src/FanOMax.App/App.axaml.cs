using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FanOMax.App.ViewModels;
using FanOMax.App.Views;

namespace FanOMax.App;

public partial class App : Application
{
    /// <summary>Argument du lancement à l'ouverture de session (scripts\install-app.ps1) : fenêtre réduite dans la barre des tâches.</summary>
    public const string MinimizedArgument = "--minimized";

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
                WindowState = desktop.Args?.Contains(MinimizedArgument, StringComparer.OrdinalIgnoreCase) == true
                    ? WindowState.Minimized
                    : WindowState.Normal,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}