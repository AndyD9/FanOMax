namespace FanOMax.Core.Analysis;

/// <summary>Un instant passé à replacer dans le journal, et l'ancienneté maximale de la mesure retenue.</summary>
public readonly record struct ShadowLookupQuery(DateTime Time, TimeSpan MaxAge);

/// <summary>
/// Mesures de FanOMax à un instant passé : la dernière ligne de chaque groupe à cet instant ou juste avant.
/// Sert à replacer un incident (erreur WHEA, plantage) dans son contexte (température, puissance).
/// Seuls les fichiers des jours concernés sont lus, en lecture partagée (le service écrit dans celui du jour).
/// </summary>
public static class ShadowLogLookup
{
    /// <returns>Pour chaque requête, dans le même ordre : une ligne par groupe (triée par nom), vide si rien ne convient.</returns>
    public static IReadOnlyList<IReadOnlyList<ShadowRecord>> LastBefore(string directory, IReadOnlyList<ShadowLookupQuery> queries)
    {
        ArgumentNullException.ThrowIfNull(queries);
        var found = queries.Select(_ => new Dictionary<string, ShadowRecord>(StringComparer.Ordinal)).ToList();

        if (queries.Count > 0 && Directory.Exists(directory))
        {
            var days = queries.SelectMany(Days).ToHashSet();
            foreach (var file in ShadowLogTail.AllFiles(directory).Where(f => days.Contains(f.Key.Day)))
            {
                Read(file.Path, record =>
                {
                    for (var i = 0; i < queries.Count; i++)
                    {
                        var query = queries[i];
                        if (record.Time <= query.Time && query.Time - record.Time <= query.MaxAge
                            && (!found[i].TryGetValue(record.Group, out var kept) || record.Time >= kept.Time))
                        {
                            found[i][record.Group] = record;
                        }
                    }
                });
            }
        }

        return found
            .Select(f => (IReadOnlyList<ShadowRecord>)f.Values.OrderBy(r => r.Group, StringComparer.Ordinal).ToList())
            .ToList();
    }

    private static IEnumerable<DateOnly> Days(ShadowLookupQuery query)
    {
        var first = DateOnly.FromDateTime(query.Time - query.MaxAge);
        for (var day = first; day <= DateOnly.FromDateTime(query.Time); day = day.AddDays(1))
        {
            yield return day;
        }
    }

    /// <summary>Parcourt le fichier ligne à ligne, sans le garder en mémoire (un jour fait plusieurs Mo).</summary>
    private static void Read(string path, Action<ShadowRecord> visit)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            ShadowLogParser? parser = null;
            while (reader.ReadLine() is { } line)
            {
                if (ShadowLogParser.IsHeader(line))
                {
                    parser = new ShadowLogParser(line);
                }
                else if (parser?.Parse(line) is { } record)
                {
                    visit(record);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fichier purgé ou inaccessible : l'incident s'affiche sans contexte.
        }
    }
}
