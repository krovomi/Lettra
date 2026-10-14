using System.Globalization;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Bootstrap;
using Lettra.UI.Abstractions;
using Lettra.UI.Gateway.InProcess;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Storage;

namespace Lettra.App.Maui;

/// <summary>
/// Composition du poste isolé (mode 1). Phase 0 : un dossier local de démonstration, base SQLite dans
/// le dossier de données de l'application. Le registre local des dossiers (fichier .lettra, clé dans
/// le coffre de l'OS, SQLCipher) arrive en phase 2.
/// </summary>
public static class MauiProgram
{
    private static readonly Guid DemoDossierId = new("0199e3a0-0000-7000-8000-00000000d051");

    public static MauiApp CreateMauiApp()
    {
        MauiAppBuilder builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<LettraApp>();
        builder.Services.AddMauiBlazorWebView();

        LettraCultures.Apply(LettraCultures.Resolve(CultureInfo.InstalledUICulture.Name));

        string database = Path.Combine(FileSystem.AppDataDirectory, $"dossier-{DemoDossierId}.db");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [LettraBootstrap.DeploymentModeKey] = nameof(DeploymentMode.IsolatedDesktop),
            ["Lettra:Dossiers:0:Id"] = DemoDossierId.ToString(),
            ["Lettra:Dossiers:0:Provider"] = nameof(DatabaseProvider.Sqlite),
            ["Lettra:Dossiers:0:ConnectionSecretRef"] = "config:LocalDossier",
            ["ConnectionStrings:LocalDossier"] = $"Data Source={database}",
        });

        builder.Services.AddLettra(builder.Configuration);
        builder.Services.AddSingleton<IDossierSelection>(new FixedDossierSelection(DemoDossierId));
        builder.Services.AddInProcessGateways();
        builder.Services.AddLettraUi();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
