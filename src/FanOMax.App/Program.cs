using Avalonia;
using System;
using FanOMax.App.Tray;

namespace FanOMax.App;

sealed class Program
{
    /// <summary>Instance unique de la session (null avant le lancement, ou dans le concepteur visuel).</summary>
    internal static SingleInstance? Instance { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            // Déjà lancée : ouvrir sa fenêtre, sauf au lancement automatique (elle est déjà dans la zone de notification).
            if (!args.Contains(App.MinimizedArgument, StringComparer.OrdinalIgnoreCase))
            {
                SingleInstance.ActivateExisting();
            }

            return;
        }

        Instance = instance;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
