namespace FanOMax.Service.Configuration;

/// <summary>Validation de la configuration : une configuration invalide est refusée et la précédente reste active.</summary>
public static class FanOMaxOptionsValidator
{
    public static List<string> Validate(FanOMaxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();

        if (options.IntervalSeconds is < 0.5 or > 5)
        {
            errors.Add($"IntervalSeconds doit être entre 0,5 et 5 s (actuel : {options.IntervalSeconds}).");
        }

        if (options.Safety.WatchdogSeconds < 2 * options.IntervalSeconds)
        {
            errors.Add("Safety.WatchdogSeconds doit valoir au moins 2 × IntervalSeconds.");
        }

        if (options.Safety.MaxConsecutiveErrors < 1)
        {
            errors.Add("Safety.MaxConsecutiveErrors doit valoir au moins 1.");
        }

        if (options.Safety.ResumeAfterSeconds < 0)
        {
            errors.Add("Safety.ResumeAfterSeconds ne peut pas être négatif.");
        }

        if (options.ShadowLog.RetentionDays is < 1 or > 90)
        {
            errors.Add("ShadowLog.RetentionDays doit être entre 1 et 90 jours.");
        }

        var enabled = options.Groups.Where(g => g.Enabled).ToList();
        foreach (var duplicate in enabled.GroupBy(g => g.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            errors.Add($"Nom de groupe en double : « {duplicate.Key} ».");
        }

        foreach (var shared in enabled.SelectMany(g => g.Controls.Distinct(StringComparer.Ordinal)).GroupBy(c => c, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            errors.Add($"La sortie PWM {shared.Key} est pilotée par plusieurs groupes.");
        }

        foreach (var group in enabled)
        {
            var label = string.IsNullOrWhiteSpace(group.Name) ? "(sans nom)" : group.Name;
            if (string.IsNullOrWhiteSpace(group.Name))
            {
                errors.Add("Un groupe n'a pas de nom.");
            }

            if (group.Controls.Count == 0)
            {
                errors.Add($"Groupe {label} : aucune sortie PWM (Controls).");
            }

            if (group.TargetTemperature is < 40 or > 95)
            {
                errors.Add($"Groupe {label} : TargetTemperature doit être entre 40 et 95 °C.");
            }

            if (group.EffectiveCriticalTemperature is < 60 or > 110)
            {
                errors.Add($"Groupe {label} : CriticalTemperature doit être entre 60 et 110 °C.");
            }

            if (group.TargetTemperature is { } target && target >= group.EffectiveCriticalTemperature)
            {
                errors.Add($"Groupe {label} : la cible doit être inférieure à la température critique.");
            }
        }

        return errors;
    }
}
