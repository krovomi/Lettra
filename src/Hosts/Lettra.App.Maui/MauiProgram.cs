using System.Globalization;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Bootstrap;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Sqlite.Local;
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
/// Composition du poste isolé (mode 1) : registre local des dossiers dans le dossier de données de l'application,
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

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [LettraBootstrap.DeploymentModeKey] = nameof(DeploymentMode.IsolatedDesktop),
            [LettraBootstrap.LocalDataDirectoryKey] = Path.Combine(FileSystem.AppDataDirectory, "dossiers"),
        });

        builder.Services.AddLettra(builder.Configuration);
        builder.Services.AddSingleton<IKekStore, SecureStorageKekStore>();
        builder.Services.AddSingleton<IDossierSelection, ActiveDossierSelection>();
        builder.Services.AddInProcessGateways();
        builder.Services.AddLettraUi();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        MauiApp app = builder.Build();
        // Avant le premier écran : cabinet local, premier dossier, dossier ouvert.
        app.Services.InitializeLettraAsync(builder.Configuration, CancellationToken.None).GetAwaiter().GetResult();
        return app;
    }

    /// <summary>Le dossier ouvert sur le poste, tenu par le registre local.</summary>
    private sealed class ActiveDossierSelection(LocalDossierRegistry registry) : IDossierSelection
    {
        public Guid? CurrentDossierId => registry.ActiveDossierId;
    }
}
