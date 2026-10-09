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

            // Plancher à 20 % : un ventilateur à l'arrêt ne refroidit plus rien.
            if (group.FixedPercent is < 20 or > 100)
            {
                errors.Add($"Groupe {label} : FixedPercent doit être entre 20 et 100 %.");
            }

            foreach (var (control, scale) in group.ControlScales)
            {
                if (!group.Controls.Contains(control, StringComparer.Ordinal))
                {
                    errors.Add($"Groupe {label} : ControlScales cite {control}, qui n'est pas dans Controls.");
                }

                if (scale is < 0.3 or > 2 || double.IsNaN(scale))
                {
                    errors.Add($"Groupe {label} : le facteur de {control} doit être entre 0,3 et 2 (actuel : {scale}).");
                }
            }

            if (group.Controls.Count > 0 && group.Controls.All(c => group.ControlScales.GetValueOrDefault(c, 1) != 1))
            {
                errors.Add($"Groupe {label} : au moins une sortie doit rester sans facteur (référence du % appliqué).");
            }
        }

        return errors;
    }
}
