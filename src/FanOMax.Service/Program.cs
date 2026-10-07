using FanOMax.Service;
using FanOMax.Service.Configuration;
using Serilog;

Directory.CreateDirectory(FanOMaxPaths.LogsDirectory);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", Serilog.Events.LogEventLevel.Information)
    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture)
    .WriteTo.File(
        Path.Combine(FanOMaxPaths.LogsDirectory, "fanomax-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        formatProvider: System.Globalization.CultureInfo.InvariantCulture,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    // config.json est surveillé : chaque enregistrement est appliqué à chaud (s'il est valide).
    builder.Configuration.AddJsonFile(FanOMaxPaths.ConfigFile, optional: true, reloadOnChange: true);
    builder.Services.Configure<FanOMaxOptions>(builder.Configuration.GetSection(FanOMaxOptions.Section));

    // Permet de tourner comme service Windows (no-op quand lancé en console pour le développement).
    builder.Services.AddWindowsService(options => options.ServiceName = "FanOMax");
    builder.Services.AddSerilog();
    builder.Services.AddHostedService<RegulationService>();

    builder.Build().Run();
    return Environment.ExitCode;
}
#pragma warning disable CA1031 // Dernier rempart : toute erreur fatale est journalisée avant l'arrêt.
catch (Exception ex)
#pragma warning restore CA1031
{
    Log.Fatal(ex, "Arrêt inattendu du service FanOMax");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
