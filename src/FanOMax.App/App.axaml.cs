using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using FanOMax.App.Tray;
using FanOMax.App.ViewModels;
using FanOMax.App.Views;

namespace FanOMax.App;

/// <summary>
/// L'interface vit dans la zone de notification : le tableau de bord s'ouvre à la demande (clic sur l'icône, menu Démarrer)
/// et se ferme sans arrêter l'interface. Les données continuent d'être lues fenêtre fermée, pour l'icône et son infobulle.
/// </summary>
public partial class App : Application, IDisposable
{
    /// <summary>Argument du lancement à l'ouverture de session : directement dans la zone de notification, sans fenêtre.</summary>
    public const string MinimizedArgument = "--minimized";

    private MainViewModel? _viewModel;
    private TrayController? _tray;
    private MainWindow? _window;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _viewModel = new MainViewModel();
            _tray = new TrayController(this, _viewModel, OpenWindow, () => desktop.Shutdown());
            desktop.Exit += (_, _) => Dispose();

            Program.Instance?.ListenForActivation(() => Dispatcher.UIThread.Post(OpenWindow));
            if (desktop.Args?.Contains(MinimizedArgument, StringComparer.OrdinalIgnoreCase) != true)
            {
                OpenWindow();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Fin de l'interface : retire l'icône de la zone de notification et arrête la lecture du journal.</summary>
    public void Dispose()
    {
        _tray?.Dispose();
        _tray = null;
        _viewModel?.Dispose();
        _viewModel = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>Ouvre le tableau de bord, ou ramène au premier plan celui déjà ouvert.</summary>
    private void OpenWindow()
    {
        if (_window is null)
        {
            // Fenêtre recréée à chaque ouverture : fermée, elle ne consomme rien (courbes comprises).
            _window = new MainWindow { DataContext = _viewModel };
            _window.Closed += (_, _) => _window = null;
            _window.Show();
            return;
        }

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }
}
