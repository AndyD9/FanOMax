using System.Diagnostics.Eventing.Reader;
using System.Xml.Linq;
using FanOMax.Core.Diagnostics;

namespace FanOMax.App.Diagnostics;

/// <summary>
/// Lecture des incidents de stabilité dans le journal Système de Windows (lisible sans droits administrateur).
/// Le journal garde l'historique : rien n'est à enregistrer côté FanOMax, et les incidents antérieurs à l'installation apparaissent aussi.
/// </summary>
internal static class SystemEventLog
{
    /// <summary>Au-delà, les plus anciens sont ignorés (une rafale d'erreurs corrigées peut en compter des milliers).</summary>
    private const int MaxEvents = 500;

    private static readonly XNamespace EventNamespace = "http://schemas.microsoft.com/win/2004/08/events/event";

    /// <summary>Incidents des <paramref name="days"/> derniers jours, du plus récent au plus ancien.</summary>
    /// <exception cref="EventLogException">Journal illisible.</exception>
    public static IReadOnlyList<StabilityEvent> ReadIncidents(int days)
    {
        var query = new EventLogQuery("System", PathType.LogName, StabilityEvents.Query(days)) { ReverseDirection = true };
        using var reader = new EventLogReader(query);
        var records = new List<SystemEventRecord>();
        while (records.Count < MaxEvents && reader.ReadEvent() is { } record)
        {
            using (record)
            {
                if (record.TimeCreated is { } time)
                {
                    records.Add(new SystemEventRecord(time, record.ProviderName, record.Id, record.Level ?? 0, Data(record)));
                }
            }
        }

        // Classement de l'ensemble : un Kernel-Power 41 se lit avec l'échec du démarrage rapide du même démarrage.
        return StabilityEvents.ClassifyAll(records);
    }

    /// <summary>Champs nommés de <c>EventData</c> (<c>ApicId</c>, <c>MCABank</c>, <c>BugcheckCode</c>…).</summary>
    private static Dictionary<string, string> Data(EventRecord record)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal);
        var eventData = XElement.Parse(record.ToXml()).Element(EventNamespace + "EventData");
        foreach (var item in eventData?.Elements(EventNamespace + "Data") ?? [])
        {
            if (item.Attribute("Name")?.Value is { } name)
            {
                data[name] = item.Value;
            }
        }

        return data;
    }
}
