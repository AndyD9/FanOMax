using System.Diagnostics;
using System.Security.Principal;

namespace FanOMax.Hardware;

/// <summary>Vérifications de l'environnement avant d'accéder au matériel.</summary>
public static class SystemChecks
{
    public const string PawnIoLibraryPath = @"C:\Program Files\PawnIO\PawnIOLib.dll";

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool IsPawnIoInstalled() => File.Exists(PawnIoLibraryPath);

    /// <summary>FanControl (Rem0o) écrit lui aussi les PWM : FanOMax ne doit jamais écrire en même temps.</summary>
    public static bool IsFanControlRunning()
    {
        var processes = Process.GetProcessesByName("FanControl");
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return processes.Length > 0;
    }
}
