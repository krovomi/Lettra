// Démo console du POC : un poste MAUI et un navigateur travaillent hors ligne, un collègue modifie
// un brouillon au bureau, puis le réseau revient et l'application demande avant de synchroniser.
// Usage : dotnet run [--auto]   (--auto répond « Détail » puis « Maintenant » sans clavier)
using System.Text.Json;
using Lettra.Poc.Sync.Client;
using Lettra.Poc.Sync.Contracts;
using Lettra.Poc.Sync.Demo;
using Lettra.Poc.Sync.Server;
using Microsoft.Extensions.DependencyInjection;

var auto = args.Contains("--auto");
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
const string url = "http://127.0.0.1:5199";

await using var server = SyncServer.Build(["--urls", url, "--Logging:LogLevel:Default=Warning"]);
await server.StartAsync();
var state = server.Services.GetRequiredService<ServerState>();
var dossier = Guid.NewGuid();
var dbDir = Directory.CreateTempSubdirectory("lettra-demo-");

var online = true;
(ClientProfile Profile, LocalStore Store, HttpSyncClient Http, OfflineInvoicing Invoicing, SyncCoordinator Sync) NewDevice(ClientProfile profile, string name)
{
    state.EnrollDevice(profile.DeviceId, profile.SeriesCode ?? "WEB");
    var store = new LocalStore(Path.Combine(dbDir.FullName, name + ".db"));
    var http = new HttpClient { BaseAddress = new Uri(url) };
    var client = new HttpSyncClient(http, profile, store, TimeProvider.System);
    client.LoginAsync("pin-1234", default).GetAwaiter().GetResult();
    var probe = new HttpNetworkProbe(http, () => online ? NetworkKind.Wifi : NetworkKind.None);
    var sync = new SyncCoordinator(store, probe, new ConsoleConsentPrompt(name, auto), client, () => SyncPreference.AlwaysAsk, TimeProvider.System);
    return (profile, store, client, new OfflineInvoicing(profile, store, TimeProvider.System), sync);
}

var maui = NewDevice(ClientProfile.Maui(Guid.NewGuid(), dossier, "P01"), "Poste P01 (MAUI)");
var web = NewDevice(ClientProfile.Browser(Guid.NewGuid(), dossier), "Navigateur");

Step("Préparation en ligne : un brouillon partagé");
var shared = maui.Invoicing.CreateDraft("SARL Dupont", [new("Mission de révision", 1, 1200m, 0.20m)]);
await maui.Sync.OnNetworkChangedAsync();
await web.Sync.OnNetworkChangedAsync();

Step("Coupure réseau : on continue à travailler");
online = false;
var a = maui.Invoicing.Validate(maui.Invoicing.CreateDraft("Boulangerie Martin", [new("Tenue comptable", 12, 150m, 0.20m)]));
var b = maui.Invoicing.Validate(maui.Invoicing.CreateDraft("Pharmacie Roux", [new("Bilan", 1, 900m, 0.20m)]));
Console.WriteLine($"  MAUI : factures validées hors ligne {a}, {b}");
maui.Invoicing.EditDraft(shared, i => { i.SetCustomerName("SARL Dupont & Fils"); i.SetNotes("Relance faite"); });
Console.WriteLine("  MAUI : brouillon SARL Dupont modifié (client, notes)");
var webDraft = web.Invoicing.CreateDraft("Garage Petit", [new("Paie", 3, 45m, 0.20m)]);
Console.WriteLine($"  Navigateur : brouillon Garage Petit, validation demandée → numéro : {web.Invoicing.Validate(webDraft) ?? "(attribué à la synchro)"}");
state.EditOnServer(shared, i => { i.SetCustomerName("Dupont SARL"); i.SetDueDate(new DateOnly(2026, 12, 1)); });
Console.WriteLine("  Bureau : un collègue renomme le client du brouillon partagé et fixe l'échéance");

Step("Le réseau revient");
online = true;
foreach (var device in new[] { maui, web })
{
    var report = await device.Sync.OnNetworkChangedAsync();
    Console.WriteLine($"  → {report.State} : faits acceptés {report.FactsAccepted}, intentions appliquées {report.IntentsApplied}, conflits {report.Conflicts}, reçus {report.Pulled}");
    foreach (var m in report.Messages) Console.WriteLine($"    · {m}");
}

Step("Écran de résolution des conflits (poste MAUI)");
foreach (var c in maui.Store.Conflicts())
    Console.WriteLine($"  {c.Field} : ma version {c.Mine.GetRawText()} | serveur {c.Server.GetRawText()} | départ {c.Base.GetRawText()}");
maui.Invoicing.ResolveConflict(shared, new Dictionary<string, FieldResolution>
{
    [DraftFields.CustomerName] = new(ConflictChoice.Custom, JsonSerializer.SerializeToElement("Dupont & Fils SARL")),
});
Console.WriteLine("  Choix : fusion saisie « Dupont & Fils SARL »");
var final = await maui.Sync.OnNetworkChangedAsync();
Console.WriteLine($"  → {final.State}");

Step("État du serveur");
foreach (var i in state.All().OrderBy(i => i.Number ?? "~"))
    Console.WriteLine($"  {i.Number ?? "(brouillon)",-20} {i.CustomerName,-22} v{i.Version} {i.TotalInclTax,10:N2} €  échéance {i.DueDate?.ToString() ?? "-"}  notes {i.Notes ?? "-"}");

await server.StopAsync();
maui.Store.Dispose();
web.Store.Dispose();
Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
dbDir.Delete(recursive: true);

static void Step(string title) => Console.WriteLine($"\n■ {title}");
