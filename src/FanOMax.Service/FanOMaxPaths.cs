namespace FanOMax.Service;

/// <summary>
/// Emplacements des fichiers du service. Par défaut <c>C:\ProgramData\FanOMax</c> ;
/// la variable d'environnement <c>FANOMAX_HOME</c> permet de le changer (développement, tests).
/// </summary>
public static class FanOMaxPaths
{
    public static string Root { get; } =
        Environment.GetEnvironmentVariable("FANOMAX_HOME") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FanOMax");

    public static string ConfigFile => Path.Combine(Root, "config.json");

    public static string LogsDirectory => Path.Combine(Root, "logs");

    public static string ShadowDirectory => Path.Combine(Root, "shadow");
}
