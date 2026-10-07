using System.Globalization;

namespace FanOMax.Probe;

/// <summary>Analyse minimale de la ligne de commande : <c>commande [argument] [--option valeur] [--drapeau]</c>.</summary>
internal sealed class CommandLine
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

    public string Command { get; private init; } = "";

    public IReadOnlyList<string> Arguments { get; private init; } = [];

    public static CommandLine Parse(string[] args)
    {
        var positional = new List<string>();
        var result = new CommandLine { Command = args.Length > 0 ? args[0].ToLowerInvariant() : "", Arguments = positional };

        for (var i = 1; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                var name = args[i][2..];
                var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
                result._options[name] = hasValue ? args[++i] : null;
            }
            else
            {
                positional.Add(args[i]);
            }
        }

        return result;
    }

    public bool Has(string name) => _options.ContainsKey(name);

    public string? Get(string name) => _options.TryGetValue(name, out var value) ? value : null;

    public double GetDouble(string name, double defaultValue) =>
        Get(name) is { } raw && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : defaultValue;

    /// <summary>Durée au format <c>90</c>, <c>90s</c>, <c>10m</c> ou <c>1h</c>.</summary>
    public TimeSpan? GetDuration(string name)
    {
        var raw = Get(name)?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        var (number, unit) = char.IsLetter(raw[^1]) ? (raw[..^1], raw[^1]) : (raw, 's');
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            throw new ArgumentException($"Durée invalide pour --{name} : « {raw} » (exemples : 90s, 10m, 1h).");
        }

        return unit switch
        {
            's' => TimeSpan.FromSeconds(value),
            'm' => TimeSpan.FromMinutes(value),
            'h' => TimeSpan.FromHours(value),
            _ => throw new ArgumentException($"Unité inconnue pour --{name} : « {unit} » (s, m ou h)."),
        };
    }
}
