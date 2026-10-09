using System.Text.Json;

namespace FanOMax.Contracts;

/// <summary>
/// État instantané écrit par le service à chaque cycle dans <c>C:\ProgramData\FanOMax\live.json</c>
/// et lu par l'interface (en attendant l'IPC de la phase 4).
/// </summary>
/// <param name="Mode">« Active » ou « Shadow ».</param>
/// <param name="Writing">Vrai si FanOMax pilote réellement les ventilateurs.</param>
public sealed record LiveSnapshot(
    DateTime Timestamp,
    string Mode,
    bool Writing,
    IReadOnlyList<LiveGroup> Groups,
    IReadOnlyList<LiveFan> Fans,
    IReadOnlyList<LiveTemperature> Temperatures)
{
    public const string FileName = "live.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Instantané lu, ou <c>null</c> si le texte est vide ou illisible.</summary>
    public static LiveSnapshot? FromJson(string json)
    {
        try
        {
            return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<LiveSnapshot>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <param name="Target">Température cible (°C).</param>
/// <param name="Percent">Consigne retenue pour les ventilateurs du groupe (%).</param>
public sealed record LiveGroup(string Name, double Target, double? Temperature, double? Power, double Percent, string Status, bool Written);

/// <summary>Une sortie PWM et son tachymètre.</summary>
/// <param name="Percent">% appliqué, lu sur le matériel.</param>
/// <param name="Rpm">Vitesse lue sur le tachymètre de même numéro (<c>null</c> sans tachymètre).</param>
/// <param name="Group">Groupe FanOMax qui pilote cette sortie, ou <c>null</c> (BIOS, pilote du GPU, FanControl).</param>
/// <param name="TargetPercent">Consigne de ce groupe (%), ou <c>null</c> si la sortie n'appartient à aucun groupe.</param>
/// <param name="Written">Vrai si FanOMax a écrit la consigne sur cette sortie pendant ce cycle.</param>
public sealed record LiveFan(string ControlId, string Name, string Hardware, double? Percent, double? Rpm, string? Group, double? TargetPercent, bool Written);

public sealed record LiveTemperature(string Id, string Name, string Hardware, double Value);
