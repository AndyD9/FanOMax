using Microsoft.Win32;

namespace FanOMax.App.Tray;

/// <summary>
/// Lancement de l'interface à l'ouverture de session (<c>HKCU\...\Run</c>, sans droits admin), dans la zone de notification.
/// Même entrée que celle écrite par <c>scripts\install-app.ps1</c>.
/// </summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FanOMax";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    /// <summary>Active (pour l'exécutable en cours) ou retire le lancement à l'ouverture de session.</summary>
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is { } exe)
        {
            key.SetValue(ValueName, $"\"{exe}\" {App.MinimizedArgument}");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
