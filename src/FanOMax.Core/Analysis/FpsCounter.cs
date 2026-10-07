namespace FanOMax.Core.Analysis;

/// <summary>
/// Calcule les FPS par application à partir d'événements « image présentée » horodatés,
/// sur une fenêtre glissante. Thread-safe.
/// </summary>
public sealed class FpsCounter(TimeSpan window)
{
    private readonly Queue<(string App, TimeSpan At)> _frames = new();
    private readonly Lock _lock = new();

    public FpsCounter() : this(TimeSpan.FromSeconds(1))
    {
    }

    public void AddFrame(string application, TimeSpan at)
    {
        lock (_lock)
        {
            _frames.Enqueue((application, at));
        }
    }

    /// <summary>Application qui présente le plus d'images sur la fenêtre (le jeu au premier plan, en pratique).</summary>
    public (string App, double Fps)? GetTop(TimeSpan now)
    {
        lock (_lock)
        {
            while (_frames.Count > 0 && now - _frames.Peek().At > window)
            {
                _frames.Dequeue();
            }

            if (_frames.Count == 0)
            {
                return null;
            }

            var top = _frames.GroupBy(f => f.App, StringComparer.OrdinalIgnoreCase)
                .Select(g => (App: g.Key, Count: g.Count()))
                .MaxBy(g => g.Count);

            return (top.App, top.Count / window.TotalSeconds);
        }
    }
}
