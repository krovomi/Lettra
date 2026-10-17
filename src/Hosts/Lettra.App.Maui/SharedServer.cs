using System.Text.Json;
using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;
using Lettra.UI.Gateway.Http;
using Lettra.UI.Gateway.Http.Device;
using Lettra.UI.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Authentication;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Storage;

namespace Lettra.App.Maui;

/// <summary>
/// Serveur commun : l'application installée (Windows, macOS, Android, iOS) travaille directement sur le serveur du
/// cabinet, par la même API et sur le même dossier que le navigateur. Tous les appareils reliés au même serveur voient
/// donc les mêmes données. L'utilisateur choisit le serveur sur l'écran « Serveur du cabinet » (seule l'adresse est
/// demandée, le reste est lu sur le serveur) ; le choix est gardé dans <see cref="SettingsFile"/> et prend effet au
/// démarrage suivant. Sans ce fichier, l'appareil reste seul (mode 1) ou relié en réplique (<see cref="ConnectedDevice"/>).
/// <para>
/// Il n'y a pas de base sur l'appareil dans ce mode : il a besoin du réseau. La base locale du poste isolé reste en
/// place, intacte, et revient quand l'utilisateur choisit « Revenir à cet appareil seul ».
/// </para>
/// </summary>
internal static class SharedServer
{
    public const string SettingsFile = "serveur-commun.json";

    /// <summary>Dossier ouvert sur le serveur, retenu d'un lancement à l'autre.</summary>
    private const string DossierPreference = "lettra.serveur-commun.dossier";

    /// <summary>Le serveur commun retenu, ou <c>null</c> : l'appareil travaille seul (ou en réplique).</summary>
    public static SharedServerSettings? Read()
    {
        string path = SettingsPath;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("Lettra", out JsonElement lettra)
                || !lettra.TryGetProperty("SharedServer", out JsonElement server))
            {
                return null;
            }

            Uri? address = Absolute(server, "Address");
            Uri? authority = Absolute(server, "Authority");
            string? clientId = server.TryGetProperty("ClientId", out JsonElement c) ? c.GetString() : null;
            return address is not null && authority is not null && !string.IsNullOrWhiteSpace(clientId)
                ? new SharedServerSettings(address, authority, clientId)
                : null;
        }
        catch (JsonException)
        {
            // Fichier abîmé : l'appareil démarre seul, l'utilisateur pourra choisir à nouveau le serveur.
            return null;
        }
    }

    /// <summary>
    /// Composition de l'application reliée au serveur commun : les gateways HTTP du navigateur, la connexion du cabinet
    /// (PKCE) et l'écran de choix du serveur. Ni base locale, ni compte local, ni synchronisation.
    /// </summary>
    public static MauiApp CreateApp(MauiAppBuilder builder, SharedServerSettings server)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<ITokenVault, SecureStorageTokenVault>();
        builder.Services.AddSingleton(AuthorizationBrowser());
        // Code PIN du poste : avant AddLettraUi, qui ne crée le verrou qu'avec un coffre.
        builder.Services.AddSingleton<IDeviceLockStore, SecureStorageDeviceLockStore>();
        builder.Services.AddSharedServerGateways(server, RememberedDossier(), RememberDossier);
        builder.Services.AddLettraUi();
        builder.Services.AddSharedServerChoice(server.Address);
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }

    /// <summary>L'écran « Serveur du cabinet » : choisir le serveur commun, ou revenir à cet appareil seul.</summary>
    public static IServiceCollection AddSharedServerChoice(this IServiceCollection services, Uri? current)
    {
        services.AddSharedServerProbe();
        services.AddSingleton<IServerConnection>(sp => new SharedServerConnection(sp.GetRequiredService<SharedServerProbe>, current));
        return services;
    }

    internal static string SettingsPath => Path.Combine(FileSystem.AppDataDirectory, SettingsFile);

    /// <summary>Enregistre le serveur retenu ; le dossier retenu pour l'ancien serveur est oublié.</summary>
    internal static void Save(SharedServerSettings server)
    {
        // Écriture à la main (pas de sérialisation par réflexion : l'application est élaguée sous Android et iOS).
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("Lettra");
            writer.WriteStartObject("SharedServer");
            writer.WriteString("Address", server.Address.ToString());
            writer.WriteString("Authority", server.Authority.ToString());
            writer.WriteString("ClientId", server.ClientId);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        string path = SettingsPath;
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, buffer.ToArray());
        File.Move(temporary, path, overwrite: true);
        RememberDossier(null);
    }

    /// <summary>Retour à cet appareil seul : le fichier du serveur commun disparaît, les données de l'appareil n'ont pas bougé.</summary>
    internal static void Forget()
    {
        File.Delete(SettingsPath);
        RememberDossier(null);
    }

    /// <summary>
    /// Connexion du cabinet par le navigateur du système. Ordinateur (Windows, macOS) : retour par la boucle locale
    /// (RFC 8252 §7.3). Téléphone et tablette (Android, iOS) : retour par le schéma <c>fr.lettra.app://</c>, l'application
    /// étant mise en veille pendant la connexion.
    /// </summary>
    private static IAuthorizationBrowser AuthorizationBrowser() =>
        DeviceInfo.Current.Platform == DevicePlatform.Android || DeviceInfo.Current.Platform == DevicePlatform.iOS
            ? new WebAuthenticatorBrowser()
            : new LoopbackAuthorizationBrowser(uri => Launcher.Default.OpenAsync(uri));

    private static Guid? RememberedDossier() =>
        Guid.TryParse(Preferences.Default.Get(DossierPreference, string.Empty), out Guid id) ? id : null;

    private static void RememberDossier(Guid? dossier)
    {
        if (dossier is Guid id)
        {
            Preferences.Default.Set(DossierPreference, id.ToString());
        }
        else
        {
            Preferences.Default.Remove(DossierPreference);
        }
    }

    private static Uri? Absolute(JsonElement section, string name) =>
        section.TryGetProperty(name, out JsonElement value) && Uri.TryCreate(value.GetString(), UriKind.Absolute, out Uri? uri) ? uri : null;
}

/// <summary>
/// Choix du serveur commun depuis l'écran « Serveur du cabinet ». Le choix prend effet au démarrage suivant ; changer de
/// serveur efface les jetons de connexion, qui ne doivent jamais partir vers un autre serveur que le leur.
/// </summary>
internal sealed class SharedServerConnection(Func<SharedServerProbe> probe, Uri? current) : IServerConnection
{
    private readonly SecureStorageTokenVault _tokens = new();

    public Uri? CurrentServer => current;

    public bool RestartPending { get; private set; }

    public ServerRestartKind RestartKind =>
        DeviceInfo.Current.Platform == DevicePlatform.WinUI ? ServerRestartKind.Automatic
        : DeviceInfo.Current.Platform == DevicePlatform.iOS ? ServerRestartKind.Manual
        : ServerRestartKind.CloseThenReopen;

    public async Task<OperationResult<Uri>> UseServerAsync(string address, CancellationToken cancellationToken)
    {
        OperationResult<SharedServerSettings> checkedServer = await probe().CheckAsync(address, cancellationToken).ConfigureAwait(false);
        if (!checkedServer.IsSuccess)
        {
            return OperationResult<Uri>.Failure(checkedServer.Errors);
        }

        SharedServerSettings server = checkedServer.Value!;
        await _tokens.ClearAsync(cancellationToken).ConfigureAwait(false);
        SharedServer.Save(server);
        RestartPending = true;
        return OperationResult<Uri>.Success(server.Address);
    }

    public async Task UseThisDeviceOnlyAsync(CancellationToken cancellationToken)
    {
        await _tokens.ClearAsync(cancellationToken).ConfigureAwait(false);
        SharedServer.Forget();
        RestartPending = true;
    }

    public void Restart()
    {
        if (OperatingSystem.IsWindows() && Environment.ProcessPath is string executable)
        {
            // Windows (application non empaquetée) : une nouvelle instance démarre, celle-ci se ferme.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = true })?.Dispose();
        }

        if (RestartKind != ServerRestartKind.Manual)
        {
            Microsoft.Maui.Controls.Application.Current?.Quit();
        }
    }
}

/// <summary>
/// Connexion du cabinet sur téléphone et tablette : <see cref="WebAuthenticator"/> (onglet de navigateur du système,
/// ASWebAuthenticationSession sous iOS) revient dans l'application par <see cref="Callback"/>, adresse de retour
/// déclarée pour le client <c>lettra-maui</c> du realm. Android reçoit le retour par <c>WebAuthenticationCallbackActivity</c>.
/// </summary>
internal sealed class WebAuthenticatorBrowser : IAuthorizationBrowser
{
    public static readonly Uri Callback = new("fr.lettra.app://callback");

    public Uri PrepareRedirectUri() => Callback;

    public async Task<Uri?> AuthorizeAsync(Uri authorizeUrl, Uri redirectUri, CancellationToken cancellationToken)
    {
        try
        {
            WebAuthenticatorResult result = await MainThread.InvokeOnMainThreadAsync(() => WebAuthenticator.Default.AuthenticateAsync(
                new WebAuthenticatorOptions { Url = authorizeUrl, CallbackUrl = redirectUri })).ConfigureAwait(false);
            // Le client OIDC relit code et state dans la requête de l'adresse de retour, comme avec la boucle locale.
            string query = string.Join('&', result.Properties.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
            return new Uri(redirectUri + "?" + query);
        }
        catch (TaskCanceledException)
        {
            // L'utilisateur a fermé la page de connexion.
            return null;
        }
    }
}
