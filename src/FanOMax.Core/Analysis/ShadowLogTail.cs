using System.Globalization;
using System.Text;

namespace FanOMax.Core.Analysis;

/// <summary>
/// Lecture incrémentale des journaux du service (<c>shadow-AAAAMMJJ.csv</c>, puis <c>-2</c>, <c>-3</c>…)
/// pendant qu'il les écrit : chaque appel à <see cref="ReadNew"/> renvoie les lignes complètes ajoutées depuis
/// l'appel précédent, en passant au fichier suivant (changement de jour, mise à jour du service) sans rien perdre.
/// Aucun droit administrateur n'est nécessaire : les fichiers sont ouverts en lecture partagée.
/// </summary>
public sealed class ShadowLogTail
{
    private readonly string _directory;
    private readonly DateOnly _firstDay;
    private (DateOnly Day, int Part)? _current;
    private long _position;
    private ShadowLogParser? _parser;

    /// <param name="firstDay">Premier jour lu au premier appel (les fichiers plus anciens sont ignorés).</param>
    public ShadowLogTail(string directory, DateOnly firstDay)
    {
        _directory = directory;
        _firstDay = firstDay;
    }

    /// <summary>Lignes complètes ajoutées depuis l'appel précédent (au premier appel : tout depuis le premier jour).</summary>
    public IReadOnlyList<ShadowRecord> ReadNew()
    {
        var records = new List<ShadowRecord>();
        if (!Directory.Exists(_directory))
        {
            return records;
        }

        foreach (var (key, path) in Files())
        {
            if (_current is { } current && key.CompareTo(current) < 0)
            {
                continue;
            }

            if (_current != key)
            {
                _current = key;
                _position = 0;
                _parser = null;
            }

            ReadFrom(path, records);
        }

        return records;
    }

    private List<((DateOnly Day, int Part) Key, string Path)> Files() =>
        Directory.EnumerateFiles(_directory, "shadow-*.csv")
            .Select(path => (Key: ParseName(path), Path: path))
            .Where(f => f.Key is { } k && k.Day >= _firstDay)
            .Select(f => (f.Key!.Value, f.Path))
            .OrderBy(f => f.Value)
            .ToList();

    /// <summary><c>shadow-AAAAMMJJ.csv</c> → (jour, 1) ; <c>shadow-AAAAMMJJ-n.csv</c> → (jour, n).</summary>
    private static (DateOnly Day, int Part)? ParseName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path)["shadow-".Length..];
        if (name.Length < 8 || !DateOnly.TryParseExact(name[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return null;
        }

        if (name.Length == 8)
        {
            return (day, 1);
        }

        return name[8] == '-' && int.TryParse(name[9..], NumberStyles.None, CultureInfo.InvariantCulture, out var part) ? (day, part) : null;
    }

    private void ReadFrom(string path, List<ShadowRecord> records)
    {
        byte[] bytes;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < _position)
            {
                // Fichier recréé : on repart du début.
                _position = 0;
                _parser = null;
            }

            stream.Seek(_position, SeekOrigin.Begin);
            bytes = new byte[stream.Length - _position];
            stream.ReadExactly(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fichier supprimé (purge) ou momentanément inaccessible : nouvel essai au prochain appel.
            return;
        }

        // Seules les lignes terminées sont lues : la dernière, en cours d'écriture, le sera au prochain appel.
        var end = Array.LastIndexOf(bytes, (byte)'\n');
        if (end < 0)
        {
            return;
        }

        _position += end + 1;
        foreach (var raw in Encoding.UTF8.GetString(bytes, 0, end).Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (ShadowLogParser.IsHeader(line))
            {
                _parser = new ShadowLogParser(line);
            }
            else if (_parser?.Parse(line) is { } record)
            {
                records.Add(record);
            }
        }
    }
}
