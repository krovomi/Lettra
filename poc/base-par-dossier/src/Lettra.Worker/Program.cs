using Lettra.Composition;
using Lettra.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Worker du POC : création des dossiers, vagues de migration, banc de charge et contrôle d'isolation.
// Usage : dotnet run --project src/Lettra.Worker -- <commande> [--option valeur]...
var command = args.FirstOrDefault() ?? "help";
var options = Options.Parse(args.Skip(1));

var config = new ConfigurationBuilder()
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
    .AddEnvironmentVariables()
    .Build();

await using var sp = new ServiceCollection()
    .AddLogging(b => b.AddConfiguration(config.GetSection("Logging")).AddSimpleConsole(o => o.SingleLine = true))
    .AddLettraPersistence(config)
    .BuildServiceProvider();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var commands = new Commands(sp, config, options, cts.Token);
return command switch
{
    "init" => await commands.InitAsync(),
    "provision" => await commands.ProvisionAsync(),
    "migrate" => await commands.MigrateAsync(),
    "downgrade" => await commands.DowngradeAsync(),
    "bench" => await commands.BenchAsync(),
    "isolation" => await commands.IsolationAsync(),
    "stats" => await commands.StatsAsync(),
    _ => Commands.Help(),
};
