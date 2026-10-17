using System.Globalization;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Bootstrap;
#if LETTRA_DEMO
using Lettra.DemoData;
#endif
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Sqlite.Local;
using Lettra.Infrastructure.Speech.OnDevice;
using Lettra.Sync.Client;
using Lettra.UI.Abstractions;
using Lettra.UI.Gateway.InProcess;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Storage;

namespace Lettra.App.Maui;

/// <summary>
/// Composition du poste isolé (mode 1, par défaut ; relié à un serveur, voir <see cref="ConnectedDevice"/>, même composition sur une réplique) : registre local des dossiers dans le dossier de données de l'application,
/// une base SQLite chiffrée (SQLite3 Multiple Ciphers) par dossier, clé du cabinet dans le coffre de l'OS.
/// Au premier lancement, le cabinet local et un premier dossier sont créés.
/// </summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        MauiAppBuilder builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<LettraApp>();
        builder.Services.AddMauiBlazorWebView();

        LettraCultures.Apply(LettraCultures.Resolve(CultureInfo.InstalledUICulture.Name));

        // Serveur commun (serveur-commun.json, écran « Serveur du cabinet ») : l'application travaille directement sur le
        // serveur du cabinet, comme le navigateur ; la base de l'appareil reste en place, inutilisée.
        if (SharedServer.Read() is { } shared)
        {
            return SharedServer.CreateApp(builder, shared);
        }

        // Relié à un serveur (lettra.json) : réplique du dossier du serveur, synchronisée ; sinon poste isolé.
        ConnectedDevice.Settings? server = ConnectedDevice.Read();
        builder.Configuration.AddInMemoryCollection(server is null
            ? new Dictionary<string, string?>
            {
                [LettraBootstrap.DeploymentModeKey] = nameof(DeploymentMode.IsolatedDesktop),
                [LettraBootstrap.LocalDataDirectoryKey] = Path.Combine(FileSystem.AppDataDirectory, "dossiers"),
            }
            : ConnectedDevice.Configuration(server));

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // Pas encore de plateforme agréée partenaire (étape 5.5) : la plateforme simulée, en mémoire, sur données fictives.
            ["Lettra:EInvoicing:Platform"] = "Simulated",
            // Le compte du poste dans le dossier de données de l'application, pas dans le dossier courant (celui du raccourci).
            ["Lettra:LocalIdentity:StorePath"] = Path.Combine(FileSystem.AppDataDirectory, "compte-local.json"),
        });
#if LETTRA_DEMO
        if (server is null)
        {
            // Version de démonstration (par défaut, voir le .csproj) : le poste isolé reçoit le jeu « Cabinet Démo » après
            // la création du compte. Jamais sur une réplique d'un dossier du serveur.
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Lettra:Demo:Seed"] = "true" });
        }
#endif

        builder.Services.AddLettra(builder.Configuration);
        // Voix sur le poste : Whisper.net, hors ligne. Le modèle (142 Mio) est téléchargé au premier usage dans le
        // dossier de l'application, puis vérifié par empreinte ; un poste sans réseau peut y déposer ggml-base.bin.
        builder.Services.AddOnDeviceSpeech(new OnDeviceSpeechOptions { ModelDirectory = Path.Combine(FileSystem.AppDataDirectory, "voix") });
        builder.Services.AddSingleton<IKekStore, SecureStorageKekStore>();
        // Jeu de démonstration en arrière-plan après l'ouverture du compte local (sans effet sans Lettra:Demo:Seed=true).
        builder.Services.AddDemoSeeding(builder.Configuration);
        // Code PIN du poste (étape 4.4) : avant AddLettraUi, qui ne crée le verrou qu'avec un coffre.
        builder.Services.AddSingleton<IDeviceLockStore, SecureStorageDeviceLockStore>();
        builder.Services.AddSingleton<IDossierSelection, ActiveDossierSelection>();
        builder.Services.AddInProcessGateways();
#if LETTRA_DEMO
        // Bouton « Charger les données de démonstration » de l'accueil (dossier vide, jamais en Production).
        builder.Services.AddDemoDataGateway("Demo", s => s.AddSingleton<IKekStore, SecureStorageKekStore>());
#endif
        if (server is not null)
        {
            builder.Services.AddSync(server);
        }

        // Écran « Serveur du cabinet » : relier cet appareil au serveur commun.
        builder.Services.AddSharedServerChoice(server?.SignIn.BaseAddress);
        builder.Services.AddLettraUi();
        if (server is not null)
        {
            // Après l'UI : la demande « Maintenant / Plus tard / Détail » passe par son dialogue.
            builder.Services.AddSyncCenter();
        }

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        MauiApp app = builder.Build();
        // Avant le premier écran : cabinet local, premier dossier (ou réplique du dossier du serveur), dossier ouvert.
        app.Services.InitializeLettraAsync(builder.Configuration, CancellationToken.None).GetAwaiter().GetResult();
        if (server is not null)
        {
            ConnectedDevice.StartSyncTriggers(app.Services);
        }

        return app;
    }

    /// <summary>Le dossier ouvert sur le poste, tenu par le registre local.</summary>
    private sealed class ActiveDossierSelection(LocalDossierRegistry registry) : IDossierSelection
    {
        public Guid? CurrentDossierId => registry.ActiveDossierId;
    }
}
