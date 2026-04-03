using AsteroidOnline.Server;
using Microsoft.Extensions.Logging;

// ── Logging ────────────────────────────────────────────────────────────────────
using var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddConsole().SetMinimumLevel(LogLevel.Information));

var logger = loggerFactory.CreateLogger<GameLoop>();

// ── Démarrage de la GameLoop ───────────────────────────────────────────────────
Console.WriteLine("=== AsteroidOnline Server ===");
Console.WriteLine("Appuyer sur Ctrl+C pour arrêter.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

using var gameLoop = new GameLoop(logger);

try
{
    await gameLoop.RunAsync(cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Serveur arrêté.");
}
