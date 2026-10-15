using System.Text.Json;
using System.Text.Json.Serialization;
using Lettra.UI.Abstractions;
using Lettra.UI.Gateway.Http;
using Lettra.UI.Gateway.Http.Device;
using Lettra.UI.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Storage;

namespace Lettra.App.Maui;

/// <summary>
/// Poste relié au serveur du cabinet ou à la cellule SaaS (modes 2, 3 et 5) : connexion au Keycloak du cabinet par le
/// navigateur système (PKCE, client <c>lettra-maui</c>), jetons dans le coffre de l'OS, gateways HTTP authentifiées.
/// Le réglage vient de <c>lettra.json</c> dans le dossier de données de l'application (écrit par l'installeur) :
/// <code>{ "Lettra": { "Server": { "BaseAddress": "https://…", "Authority": "https://…/realms/lettra", "DossierId": "…" } } }</code>
/// Sans ce fichier, le poste reste isolé (mode 1). Le travail hors ligne de ces modes arrive avec la synchronisation (phase 4).
/// </summary>
internal static class ConnectedDevice
{
    public const string SettingsFile = "lettra.json";

    public static bool TryConfigure(MauiAppBuilder builder)
    {
        (DeviceSignInOptions options, Guid? dossier) = Read(Path.Combine(FileSystem.AppDataDirectory, SettingsFile));
        if (!options.IsConfigured)
        {
            return false;
        }

        builder.Services.AddSingleton<ITokenVault, SecureStorageTokenVault>();
        builder.Services.AddSingleton<IAuthorizationBrowser>(_ => new LoopbackAuthorizationBrowser(uri => Launcher.Default.OpenAsync(uri)));
        builder.Services.AddSingleton<IDossierSelection>(new FixedDossierSelection(dossier));
        builder.Services.AddDeviceHttpGateways(options);
        builder.Services.AddLettraUi();
        return true;
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
