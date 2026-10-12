using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FanOMax.Service;

/// <summary>
/// Notifications de mise en veille et de reprise de Windows (<c>PowerRegisterSuspendResumeNotification</c>).
/// <para>
/// Un arrêt normal arrête le service, qui rend alors la main au BIOS. Mais une mise en veille, une hibernation
/// ou un arrêt avec démarrage rapide (le cœur du système est mis en veille prolongée) n'arrêtent pas le service :
/// sans ces notifications, les ventilateurs restent figés sur la dernière consigne de FanOMax.
/// </para>
/// <para>
/// Windows appelle le rappel de façon synchrone et attend son retour avant de suspendre le système :
/// le retour au BIOS est donc terminé avant la mise en veille. Fonctionne en service comme en console.
/// </para>
/// </summary>
internal sealed class PowerNotifications : IDisposable
{
    private const int DeviceNotifyCallback = 2;
    private const int PbtApmSuspend = 0x4;
    private const int PbtApmResumeSuspend = 0x7;
    private const int PbtApmResumeAutomatic = 0x12;

    private readonly Action _onSuspend;
    private readonly Action _onResume;

    // Gardé en champ : le ramasse-miettes ne doit pas collecter le délégué tant que Windows peut l'appeler.
    private readonly DeviceNotifyCallbackRoutine _callback;
    private IntPtr _registration;

    private PowerNotifications(Action onSuspend, Action onResume)
    {
        _onSuspend = onSuspend;
        _onResume = onResume;
        _callback = OnNotification;
        var parameters = new DeviceNotifySubscribeParameters
        {
            Callback = Marshal.GetFunctionPointerForDelegate(_callback),
            Context = IntPtr.Zero,
        };

        var error = NativeMethods.PowerRegisterSuspendResumeNotification(DeviceNotifyCallback, ref parameters, out _registration);
        if (error != 0)
        {
            throw new Win32Exception((int)error);
        }
    }

    /// <summary>Abonnement aux notifications. <paramref name="onSuspend"/> doit rendre la main rapidement.</summary>
    /// <exception cref="Win32Exception">Abonnement refusé par Windows.</exception>
    public static PowerNotifications Register(Action onSuspend, Action onResume) => new(onSuspend, onResume);

    public void Dispose()
    {
        if (_registration != IntPtr.Zero)
        {
            _ = NativeMethods.PowerUnregisterSuspendResumeNotification(_registration);
            _registration = IntPtr.Zero;
        }

        GC.KeepAlive(_callback);
    }

    private uint OnNotification(IntPtr context, uint type, IntPtr setting)
    {
        try
        {
            switch (type)
            {
                case PbtApmSuspend:
                    _onSuspend();
                    break;

                // Reprise automatique (toujours envoyée) puis, si un utilisateur est présent, reprise « utilisateur ».
                case PbtApmResumeAutomatic or PbtApmResumeSuspend:
                    _onResume();
                    break;
            }
        }
#pragma warning disable CA1031 // Une exception qui remonterait dans le code natif de Windows terminerait le service.
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        return 0;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint DeviceNotifyCallbackRoutine(IntPtr context, uint type, IntPtr setting);

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceNotifySubscribeParameters
    {
        public IntPtr Callback;
        public IntPtr Context;
    }

    private static class NativeMethods
    {
        [DllImport("powrprof.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint PowerRegisterSuspendResumeNotification(int flags, ref DeviceNotifySubscribeParameters recipient, out IntPtr registrationHandle);

        [DllImport("powrprof.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint PowerUnregisterSuspendResumeNotification(IntPtr registrationHandle);
    }
}
