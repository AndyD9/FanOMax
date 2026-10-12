namespace FanOMax.App.Tray;

/// <summary>
/// Une seule interface par session : un second lancement (menu Démarrer alors que FanOMax est dans la zone de notification)
/// demande à la première d'ouvrir sa fenêtre, puis s'arrête.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\FanOMax.App";
    private const string ShowEventName = @"Local\FanOMax.App.Show";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _show;
    private RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
        _show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
    }

    /// <summary>Première instance de la session, ou <c>null</c> si l'interface tourne déjà.</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
        if (created)
        {
            return new SingleInstance(mutex);
        }

        mutex.Dispose();
        return null;
    }

    /// <summary>Demande à l'instance déjà lancée d'ouvrir sa fenêtre.</summary>
    public static void ActivateExisting()
    {
        if (EventWaitHandle.TryOpenExisting(ShowEventName, out var show))
        {
            using (show)
            {
                show.Set();
            }
        }
    }

    /// <summary><paramref name="activate"/> est appelé (hors du fil de l'interface) à chaque nouveau lancement.</summary>
    public void ListenForActivation(Action activate) =>
        _registration = ThreadPool.RegisterWaitForSingleObject(_show, (_, _) => activate(), null, Timeout.Infinite, executeOnlyOnce: false);

    /// <summary>À appeler depuis le fil qui a créé l'instance (le mutex lui appartient).</summary>
    public void Dispose()
    {
        _registration?.Unregister(null);
        _show.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
