using System.Text.Json;
using System.Text.Json.Serialization;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Bootstrap;
using Lettra.Sync.Client;
using Lettra.UI.Gateway.Http;
using Lettra.UI.Gateway.Http.Device;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

namespace Lettra.App.Maui;

/// <summary>
/// Poste relié au serveur du cabinet ou à la cellule SaaS (modes 2, 3 et 5 vus du poste, mode 4 pour la composition) :
/// le poste travaille sur une réplique chiffrée du dossier du serveur, comme un poste isolé, et l'envoie au serveur par la
/// synchronisation (ADR-0008). La connexion au Keycloak du cabinet (PKCE, client <c>lettra-maui</c>, jetons dans le
/// coffre de l'OS) ne sert qu'à se synchroniser. Le réglage vient de <c>lettra.json</c> dans le dossier de données de
/// l'application (écrit par l'installeur) :
/// <code>{ "Lettra": { "Server": { "BaseAddress": "https://…", "Authority": "https://…/realms/lettra", "DossierId": "…" } } }</code>
/// Sans ce fichier, le poste reste isolé (mode 1).
/// </summary>
internal static partial class ConnectedDevice
{
    public const string SettingsFile = "lettra.json";

    /// <summary>Identifiant du poste : il porte la série de factures du poste sur le serveur, il n'est jamais régénéré.</summary>
    private const string DeviceIdPreference = "lettra.sync.device-id";

    public sealed record Settings(DeviceSignInOptions SignIn, Guid DossierId);

    /// <summary>Le réglage du poste relié, ou <c>null</c> pour un poste isolé.</summary>
    public static Settings? Read()
    {
        (DeviceSignInOptions options, Guid? dossier) = Read(Path.Combine(FileSystem.AppDataDirectory, SettingsFile));
        if (!options.IsConfigured)
        {
            return null;
        }

        return dossier is Guid id
            ? new Settings(options, id)
            : throw new InvalidOperationException($"{SettingsFile} : Lettra:Server:DossierId est obligatoire sur un poste relié.");
    }

    /// <summary>Configuration de la composition : réplique du dossier du serveur dans le dossier de données du poste.</summary>
    public static Dictionary<string, string?> Configuration(Settings settings) => new()
    {
        [LettraBootstrap.DeploymentModeKey] = nameof(DeploymentMode.HybridCloud),
        [LettraBootstrap.LocalDataDirectoryKey] = Path.Combine(FileSystem.AppDataDirectory, "replique"),
        [LettraBootstrap.ReplicaDossierIdKey] = settings.DossierId.ToString(),
    };

    /// <summary>Après <c>AddLettra</c> : connexion au serveur, transport authentifié et client de synchronisation.</summary>
    public static IServiceCollection AddSync(this IServiceCollection services, Settings settings)
    {
        services.AddSingleton<ITokenVault, SecureStorageTokenVault>();
        services.AddSingleton<IAuthorizationBrowser>(_ => new LoopbackAuthorizationBrowser(uri => Launcher.Default.OpenAsync(uri)));
        services.AddDeviceSignIn(settings.SignIn);
        services.AddHttpClient<ISyncTransport, HttpSyncTransport>(client => client.BaseAddress = settings.SignIn.BaseAddress)
            .AddHttpMessageHandler<BearerTokenHandler>();
        services.AddHttpClient<INetworkProbe, ConnectivityNetworkProbe>(client =>
        {
            client.BaseAddress = settings.SignIn.BaseAddress;
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddSyncClient(o => o.DeviceId = DeviceId());
        return services;
    }

    /// <summary>
    /// Relance la synchronisation au démarrage et à chaque changement de réseau signalé par l'OS. La sonde vérifie que le
    /// serveur répond ; un cycle déjà en cours fait ignorer l'appel, et la préférence décide s'il faut demander.
    /// </summary>
    public static void StartSyncTriggers(IServiceProvider services)
    {
        SyncCoordinator coordinator = services.GetRequiredService<SyncCoordinator>();
        ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ConnectedDevice));
        Connectivity.Current.ConnectivityChanged += (_, _) => Trigger(coordinator, logger);
        Trigger(coordinator, logger);
    }

    private static void Trigger(SyncCoordinator coordinator, ILogger logger) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await coordinator.NetworkAvailableAsync().ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Un cycle raté ne doit pas faire tomber l'application : le suivant reprendra l'outbox.
            catch (Exception e)
#pragma warning restore CA1031
            {
                LogSyncFailed(logger, e);
            }
        });

    [LoggerMessage(Level = LogLevel.Warning, Message = "Synchronisation au retour du réseau impossible.")]
    private static partial void LogSyncFailed(ILogger logger, Exception exception);

    private static Guid DeviceId()
    {
        if (Guid.TryParse(Preferences.Default.Get(DeviceIdPreference, string.Empty), out Guid id) && id != Guid.Empty)
        {
            return id;
        }

        id = Guid.CreateVersion7();
        Preferences.Default.Set(DeviceIdPreference, id.ToString());
        return id;
    }

    private static (DeviceSignInOptions Options, Guid? Dossier) Read(string path)
    {
        DeviceSignInOptions options = new();
        if (!File.Exists(path))
        {
            return (options, null);
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("Lettra", out JsonElement lettra) || !lettra.TryGetProperty("Server", out JsonElement server))
        {
            return (options, null);
        }

        options.BaseAddress = Absolute(server, "BaseAddress");
        options.Authority = Absolute(server, "Authority");
        Guid? dossier = server.TryGetProperty("DossierId", out JsonElement d) && Guid.TryParse(d.GetString(), out Guid id) ? id : null;
        return (options, dossier);
    }

    private static Uri? Absolute(JsonElement section, string name) =>
        section.TryGetProperty(name, out JsonElement value) && Uri.TryCreate(value.GetString(), UriKind.Absolute, out Uri? uri) ? uri : null;
}

/// <summary>
/// Sonde du poste : le type de réseau vient de l'OS (MAUI <see cref="Connectivity"/>), et le serveur doit répondre à
/// <c>/health</c> (un réseau « revenu » derrière un portail captif ne suffit pas).
/// </summary>
internal sealed class ConnectivityNetworkProbe(HttpClient http) : INetworkProbe
{
    public async Task<NetworkStatus> ProbeAsync(CancellationToken cancellationToken)
    {
        NetworkKind kind = Kind(Connectivity.Current);
        if (kind == NetworkKind.None)
        {
            return new NetworkStatus(false, kind);
        }

        try
        {
            using HttpResponseMessage response = await http.GetAsync(new Uri("health", UriKind.Relative), cancellationToken).ConfigureAwait(false);
            return new NetworkStatus(response.IsSuccessStatusCode, kind);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new NetworkStatus(false, kind);
        }
    }

    private static NetworkKind Kind(IConnectivity connectivity)
    {
        if (connectivity.NetworkAccess is not (NetworkAccess.Internet or NetworkAccess.ConstrainedInternet or NetworkAccess.Local))
        {
            return NetworkKind.None;
        }

        // L'OS passe par le filaire ou le Wi-Fi quand ils sont là, avant le modem mobile.
        IEnumerable<ConnectionProfile> profiles = connectivity.ConnectionProfiles;
        return profiles.Contains(ConnectionProfile.Ethernet) ? NetworkKind.Ethernet
            : profiles.Contains(ConnectionProfile.WiFi) ? NetworkKind.Wifi
            : profiles.Contains(ConnectionProfile.Cellular) ? NetworkKind.Cellular
            : NetworkKind.Ethernet;
    }
}

/// <summary>Jetons du poste dans le coffre de l'OS (MAUI <see cref="SecureStorage"/> : DPAPI sous Windows).</summary>
internal sealed class SecureStorageTokenVault : ITokenVault
{
    private const string Key = "lettra.device.tokens";

    public async Task<DeviceTokens?> ReadAsync(CancellationToken cancellationToken)
    {
        string? stored = await SecureStorage.Default.GetAsync(Key).ConfigureAwait(false);
        return stored is null ? null : JsonSerializer.Deserialize(stored, DeviceTokensJsonContext.Default.DeviceTokens);
    }

    public Task WriteAsync(DeviceTokens tokens, CancellationToken cancellationToken) =>
        SecureStorage.Default.SetAsync(Key, JsonSerializer.Serialize(tokens, DeviceTokensJsonContext.Default.DeviceTokens));

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        SecureStorage.Default.Remove(Key);
        return Task.CompletedTask;
    }
}

[JsonSerializable(typeof(DeviceTokens))]
internal sealed partial class DeviceTokensJsonContext : JsonSerializerContext;
