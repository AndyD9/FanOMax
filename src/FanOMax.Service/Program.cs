using FanOMax.Service;

var builder = Host.CreateApplicationBuilder(args);

// Permet de tourner comme service Windows (no-op quand lancé en console pour le développement).
builder.Services.AddWindowsService(options => options.ServiceName = "FanOMax");

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
