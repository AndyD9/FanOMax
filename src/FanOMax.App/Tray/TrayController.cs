using Avalonia;
using Avalonia.Controls;
using FanOMax.App.ViewModels;

namespace FanOMax.App.Tray;

/// <summary>
/// Icône de la zone de notification : sa couleur résume l'état (vert = pilotage, gris = fantôme, ambre = à regarder,
/// rouge = alerte), l'infobulle et la première ligne du menu donnent la température et la ventilation de chaque groupe.
/// Un clic ouvre le tableau de bord ; « Quitter » ferme l'interface, pas le service.
/// </summary>
internal sealed class TrayController : IDisposable
{
    /// <summary>Longueur maximale d'une infobulle de la zone de notification (Windows : 128 caractères, zéro final compris).</summary>
    private const int MaxToolTip = 127;

    private readonly MainViewModel _viewModel;
    private readonly Dictionary<TrayState, WindowIcon> _icons = [];
    private readonly TrayIcon _tray;
    private readonly NativeMenuItem _status;
    private readonly NativeMenuItem _autostart;
    private TrayState? _state;

    public TrayController(Application application, MainViewModel viewModel, Action open, Action quit)
    {
        _viewModel = viewModel;
        foreach (var state in Enum.GetValues<TrayState>())
        {
            _icons[state] = TrayIconRenderer.Render(state);
        }

        _status = new NativeMenuItem("FanOMax") { IsEnabled = false };
        var show = new NativeMenuItem("Ouvrir le tableau de bord");
        show.Click += (_, _) => open();
        _autostart = new NativeMenuItem("Démarrer avec Windows") { ToggleType = MenuItemToggleType.CheckBox, IsChecked = Autostart.IsEnabled };
        _autostart.Click += (_, _) => ToggleAutostart();
        var exit = new NativeMenuItem("Quitter l'interface (la régulation continue)");
        exit.Click += (_, _) => quit();

        _tray = new TrayIcon
        {
            Icon = _icons[TrayState.Shadow],
            ToolTipText = "FanOMax",
            Menu = [_status, show, new NativeMenuItemSeparator(), _autostart, new NativeMenuItemSeparator(), exit],
            IsVisible = true,
        };
        _tray.Clicked += (_, _) => open();
        TrayIcon.SetIcons(application, [_tray]);

        _viewModel.Refreshed += OnRefreshed;
        OnRefreshed(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _viewModel.Refreshed -= OnRefreshed;
        _tray.Dispose();
    }

    private void OnRefreshed(object? sender, EventArgs e)
    {
        var state = State(_viewModel);
        if (state != _state)
        {
            _state = state;
            _tray.Icon = _icons[state];
        }

        var lines = new List<string> { "FanOMax · " + _viewModel.ModeText };
        lines.AddRange(_viewModel.Groups.Select(g =>
            $"{g.Name} {g.Temperature}" + (g.Target.Length > 0 ? $" ({g.Target})" : "") + $" · {g.Fan}"
            + (g.Status is "" or "Normal" ? "" : $" · {g.Status}")));
        if (_viewModel.Stability.IsAlert || _viewModel.Stability.IsWarning)
        {
            lines.Add("Stabilité : " + _viewModel.Stability.Status);
        }

        var text = string.Join("\n", lines);
        _tray.ToolTipText = text.Length <= MaxToolTip ? text : text[..(MaxToolTip - 1)] + "…";
        _status.Header = lines.Count > 1 ? lines[0] + " — " + lines[1] : lines[0];
    }

    /// <summary>Alerte d'abord (service ou groupe), puis ce qui mérite un coup d'œil, puis le mode du service.</summary>
    private static TrayState State(MainViewModel vm)
    {
        if (!vm.HasData || vm.IsStale || vm.Groups.Any(g => g.IsAlert))
        {
            return TrayState.Alert;
        }

        // Écriture bloquée (mode Active sans écriture), ou incident dans le journal Windows.
        if ((!vm.IsPiloting && !vm.IsShadow) || vm.Stability.IsAlert || vm.Stability.IsWarning)
        {
            return TrayState.Warning;
        }

        return vm.IsPiloting ? TrayState.Piloting : TrayState.Shadow;
    }

    private void ToggleAutostart()
    {
        try
        {
            Autostart.Set(!Autostart.IsEnabled);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Registre inaccessible : la case reflète l'état réel ci-dessous.
        }

        _autostart.IsChecked = Autostart.IsEnabled;
    }
}
