// Vérification de bout en bout du serveur commun, côté appareil :
//   1. l'adresse saisie (par défaut localhost:8080) est vérifiée comme sur l'écran « Serveur du cabinet » ;
//   2. connexion du cabinet (PKCE, client lettra-maui) : la page de Keycloak est ouverte par appareil.mjs connexion,
//      qui saisit mot de passe et code TOTP, puis revient par la boucle locale ;
//   3. le dossier ouvert est celui du navigateur ; le client saisi dans le navigateur est vu par l'appareil ;
//   4. un client saisi sur l'appareil, que appareil.mjs voir retrouve ensuite dans le navigateur.
// Usage : dotnet run --project deploy/demo/e2e/serveur-commun -- "<client saisi dans le navigateur>" [adresse]
// Variables : SORTIE (dossier de parcours.mjs, ./sortie par défaut) ; LETTRA_APPAREIL_URL_FICHIER pour écrire l'adresse
// de connexion dans un fichier au lieu de lancer node (navigateur hors de ce conteneur).
using System.Diagnostics;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Identity;
using Lettra.UI.Abstractions;
using Lettra.UI.Gateway.Http;
using Lettra.UI.Gateway.Http.Device;
using Microsoft.Extensions.DependencyInjection;

string browserCustomer = args.Length > 0 ? args[0] : throw new ArgumentException("Nom du client saisi dans le navigateur attendu.");
string typed = args.Length > 1 ? args[1] : "localhost:8080";
string output = Environment.GetEnvironmentVariable("SORTIE") ?? "sortie";
string script = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "appareil.mjs");

using HttpClient probeClient = new();
OperationResult<SharedServerSettings> checkedServer = await new SharedServerProbe(probeClient).CheckAsync(typed, default);
Require(checkedServer.IsSuccess, "vérification de l'adresse", checkedServer.Errors);
SharedServerSettings server = checkedServer.Value!;
Log($"adresse « {typed} » vérifiée : {server.Address}, connexion {server.Authority}, client {server.ClientId}");

ServiceCollection services = new();
services.AddLogging();
services.AddSingleton<ITokenVault, InMemoryTokenVault>();
services.AddSingleton<IAuthorizationBrowser>(new LoopbackAuthorizationBrowser(OpenAsync));
Guid? remembered = null;
services.AddSharedServerGateways(server, null, d => remembered = d);
await using ServiceProvider device = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

OperationResult<SessionResponse> before = await device.GetRequiredService<ISessionGateway>().GetAsync(default);
Require(before.Value is { IsAuthenticated: false }, "session vide au premier lancement", before.Errors);
OperationResult<SessionResponse> signedIn = await device.GetRequiredService<IInteractiveSignIn>().SignInAsync(default);
Require(signedIn.IsSuccess, "connexion du cabinet", signedIn.Errors);
Guid? dossier = device.GetRequiredService<IDossierSelection>().CurrentDossierId;
Require(dossier is not null && dossier == remembered, "dossier ouvert et retenu", []);
Log($"connecté comme « {signedIn.Value!.DisplayName} », dossier {dossier} ouvert et retenu");

ICustomersGateway customers = device.GetRequiredService<ICustomersGateway>();
OperationResult<PagedResponse<CustomerSummary>> list = await customers.ListAsync(1, 200, default);
Require(list.IsSuccess, "liste des clients", list.Errors);
Require(list.Value!.Items.Any(c => c.Name == browserCustomer), $"client « {browserCustomer} » saisi dans le navigateur, vu par l'appareil", []);
Log($"l'appareil voit le client « {browserCustomer} » saisi dans le navigateur ({list.Value.TotalCount} clients)");

string name = $"Client appareil {DateTime.UtcNow:yyyyMMddHHmmss}";
OperationResult<CustomerResponse> created = await customers.CreateAsync(new CreateCustomerRequest
{
    Kind = CustomerKindDto.Individual,
    Name = name,
    Email = "contact@appareil.example",
    AddressLine1 = "2 rue du Serveur Commun",
    PostalCode = "69002",
    City = "Lyon",
}, default);
Require(created.IsSuccess, "création d'un client sur l'appareil", created.Errors);
Directory.CreateDirectory(output);
File.WriteAllText(Path.Combine(output, "client-appareil.txt"), name);
Log($"client « {name} » créé sur l'appareil (à retrouver dans le navigateur : node appareil.mjs voir)");
return 0;

// La page de connexion du cabinet, dans Chromium piloté par appareil.mjs (le « navigateur du système »).
async Task OpenAsync(Uri authorize)
{
    if (Environment.GetEnvironmentVariable("LETTRA_APPAREIL_URL_FICHIER") is { Length: > 0 } file)
    {
        await File.WriteAllTextAsync(file, authorize.ToString());
        return;
    }

    ProcessStartInfo start = new("node", [Path.GetFullPath(script), "connexion", authorize.ToString()]) { UseShellExecute = false };
    // Pas d'attente : le retour arrive par la boucle locale, que LoopbackAuthorizationBrowser écoute déjà.
    Process.Start(start)?.Dispose();
}

static void Require(bool condition, string step, IReadOnlyList<ErrorDetail> errors)
{
    if (!condition)
    {
        throw new InvalidOperationException($"ÉCHEC : {step} ({string.Join(", ", errors.Select(e => e.Code))})");
    }
}

static void Log(string message) => Console.WriteLine($"[appareil {DateTimeOffset.UtcNow:O}] {message}");
