using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Catalog;
using Lettra.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Catalog;

/// <summary>Où la cellule tient les clés des cabinets (section <c>Lettra:Kms</c>).</summary>
public sealed class KmsOptions
{
    public const string SectionName = "Lettra:Kms";

    public KmsProvider Provider { get; set; } = KmsProvider.Configuration;

    public TransitOptions Transit { get; init; } = new();
}

public enum KmsProvider
{
    /// <summary>KEK déclarées dans le coffre de la cellule (<c>Lettra:Organisations:n:Kek</c>) : serveur de cabinet, démonstration.</summary>
    Configuration = 1,

    /// <summary>Moteur Transit d'OpenBao (ou de Vault) : une clé racine par cabinet, qui ne quitte jamais le KMS.</summary>
    Transit = 2,
}

/// <summary>Moteur Transit (OpenBao, API compatible Vault). Le jeton vient du coffre de la cellule, jamais de Git.</summary>
public sealed class TransitOptions
{
    public Uri? Address { get; set; }

    public string? Token { get; set; }

    /// <summary>Point de montage du moteur Transit.</summary>
    public string Mount { get; set; } = "transit";

    /// <summary>Préfixe du nom de la clé racine de chaque cabinet, suivi de son identifiant.</summary>
    public string KeyPrefix { get; set; } = "lettra-cabinet-";

    /// <summary>Rotation automatique de chaque clé racine par le KMS (durée au format OpenBao, vide : jamais).</summary>
    public string? AutoRotatePeriod { get; set; } = "8760h";

    /// <summary>Durée de vie d'une KEK ouverte dans le cache de l'API ou du Worker.</summary>
    public TimeSpan CacheLifetime { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// KEK des cabinets tenues par le KMS de la cellule (ADR-0011, plan du mode 5 étape 1). Chaque cabinet a sa clé racine
/// dans le moteur Transit ; sa KEK de travail est tirée par le KMS (<c>datakey</c>) et seule son enveloppe est gardée,
/// dans le catalogue. L'API et le Worker ne voient la KEK qu'en mémoire, quelques minutes, effacée à l'éviction.
/// <para>
/// Détruire la clé racine d'un cabinet dans le KMS (<see cref="DestroyAsync"/>) rend son enveloppe impossible à ouvrir :
/// tous ses dossiers deviennent illisibles, sauvegardes de la cellule comprises. Une rotation de la clé racine
/// réenveloppe la KEK (<see cref="RewrapAsync"/>) sans la changer : DEK, sceaux et données restent intacts.
/// </para>
/// </summary>
internal sealed class TransitKekStore(
    IHttpClientFactory httpClients,
    IOptions<KmsOptions> options,
    IDbContextFactory<CatalogDbContext> catalogFactory) : IKekStore, ICabinetKeyVault, IDisposable
{
    public const string HttpClientName = "lettra-kms";
    private const int KekBits = 256;
    private readonly TransitOptions _transit = options.Value.Transit;
    private readonly KeyCache<Guid> _cache = new(capacity: 1024, lifetime: options.Value.Transit.CacheLifetime);

    public async ValueTask<byte[]> GetOrCreateAsync(OrganisationId organisation, CancellationToken cancellationToken)
    {
        if (await FindAsync(organisation, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            return existing;
        }

        // Une enveloppe qui ne s'ouvre plus (clé racine détruite) n'est jamais remplacée : le cabinet reste effacé.
        if (await WrappedKekAsync(organisation, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new CryptographicException("Clé maîtresse du cabinet détruite dans le KMS : ses dossiers sont illisibles.");
        }

        await EnsureRootKeyAsync(organisation, cancellationToken).ConfigureAwait(false);
        TransitData created = await PostAsync($"datakey/plaintext/{KeyName(organisation)}", new TransitRequest { Context = Context(organisation), Bits = KekBits }, cancellationToken)
            .ConfigureAwait(false);
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            // Deux instances peuvent créer la KEK en même temps : la première enveloppe enregistrée l'emporte.
            int stored = await db.Organisations.Where(o => o.Id == organisation.Value && o.WrappedKek == null)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.WrappedKek, created.Ciphertext), cancellationToken).ConfigureAwait(false);
            if (stored == 0)
            {
                return await FindAsync(organisation, cancellationToken).ConfigureAwait(false)
                    ?? throw new CryptographicException("Cabinet absent du catalogue : clé maîtresse non créée.");
            }
        }

        byte[] kek = Convert.FromBase64String(created.Plaintext!);
        _cache.Set(organisation.Value, kek);
        return kek;
    }

    public async ValueTask<byte[]?> FindAsync(OrganisationId organisation, CancellationToken cancellationToken)
    {
        if (_cache.Find(organisation.Value) is { } cached)
        {
            return cached;
        }

        string? wrapped = await WrappedKekAsync(organisation, cancellationToken).ConfigureAwait(false);
        if (wrapped is null)
        {
            return null;
        }

        TransitData? opened = await TryPostAsync($"decrypt/{KeyName(organisation)}", new TransitRequest { Ciphertext = wrapped, Context = Context(organisation) }, cancellationToken)
            .ConfigureAwait(false);
        if (opened?.Plaintext is null)
        {
            // Clé racine détruite (ou enveloppe altérée) : la KEK n'existe plus, les dossiers du cabinet sont illisibles.
            return null;
        }

        byte[] kek = Convert.FromBase64String(opened.Plaintext);
        _cache.Set(organisation.Value, kek);
        return kek;
    }

    public async Task<bool> RewrapAsync(OrganisationId organisation, CancellationToken cancellationToken)
    {
        string? wrapped = await WrappedKekAsync(organisation, cancellationToken).ConfigureAwait(false);
        if (wrapped is null)
        {
            return false;
        }

        // Clé racine détruite (départ du cabinet en cours) : l'enveloppe ne s'ouvrira plus, il n'y a rien à renouveler et
        // les autres cabinets de la passe ne doivent pas en pâtir. Déjà enveloppée par la dernière version : rien à
        // réécrire non plus (le KMS rendrait une autre enveloppe, tout aussi bonne).
        if (await TryGetAsync($"keys/{KeyName(organisation)}", cancellationToken).ConfigureAwait(false) is not { } key
            || wrapped.StartsWith($"vault:v{key.LatestVersion}:", StringComparison.Ordinal))
        {
            return false;
        }

        TransitData rewrapped = await PostAsync($"rewrap/{KeyName(organisation)}", new TransitRequest { Ciphertext = wrapped, Context = Context(organisation) }, cancellationToken)
            .ConfigureAwait(false);

        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await db.Organisations.Where(o => o.Id == organisation.Value && o.WrappedKek == wrapped)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.WrappedKek, rewrapped.Ciphertext), cancellationToken).ConfigureAwait(false) == 1;
        }
    }

    public async Task<int> RewrapAllAsync(CancellationToken cancellationToken)
    {
        List<Guid> organisations;
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            organisations = await db.Organisations.AsNoTracking().Where(o => o.WrappedKek != null).Select(o => o.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        int rewrapped = 0;
        foreach (Guid organisation in organisations)
        {
            rewrapped += await RewrapAsync(new OrganisationId(organisation), cancellationToken).ConfigureAwait(false) ? 1 : 0;
        }

        return rewrapped;
    }

    public async Task RotateAsync(OrganisationId organisation, CancellationToken cancellationToken)
    {
        await PostAsync($"keys/{KeyName(organisation)}/rotate", new TransitRequest(), cancellationToken).ConfigureAwait(false);
        await RewrapAsync(organisation, cancellationToken).ConfigureAwait(false);
    }

    public async Task DestroyAsync(OrganisationId organisation, CancellationToken cancellationToken)
    {
        _cache.Remove(organisation.Value);
        // Clé déjà détruite (départ repris après une panne) : rien à faire.
        if (await TryPostAsync($"keys/{KeyName(organisation)}/config", new TransitRequest { DeletionAllowed = true }, cancellationToken).ConfigureAwait(false) is null)
        {
            return;
        }

        using HttpClient http = httpClients.CreateClient(HttpClientName);
        using HttpResponseMessage response = await http.SendAsync(Request(HttpMethod.Delete, $"keys/{KeyName(organisation)}", content: null), cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    public void Dispose() => _cache.Dispose();

    private async Task<string?> WrappedKekAsync(OrganisationId organisation, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await db.Organisations.AsNoTracking().Where(o => o.Id == organisation.Value).Select(o => o.WrappedKek)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Clé racine du cabinet : AES-256-GCM, dérivée par contexte (l'identifiant du cabinet est lié à chaque enveloppe),
    /// non exportable. La créer quand elle existe déjà ne change rien.
    /// </summary>
    private async Task EnsureRootKeyAsync(OrganisationId organisation, CancellationToken cancellationToken)
    {
        await PostAsync($"keys/{KeyName(organisation)}", new TransitRequest { Type = "aes256-gcm96", Derived = true, Exportable = false }, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(_transit.AutoRotatePeriod))
        {
            await PostAsync($"keys/{KeyName(organisation)}/config", new TransitRequest { AutoRotatePeriod = _transit.AutoRotatePeriod }, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<TransitData> PostAsync(string path, TransitRequest body, CancellationToken cancellationToken) =>
        await TryPostAsync(path, body, cancellationToken, throwOnRefusal: true).ConfigureAwait(false) ?? new TransitData();

    /// <summary>Lecture au KMS : <c>null</c> si la clé n'existe pas (404) ; une panne lève.</summary>
    private async Task<TransitData?> TryGetAsync(string path, CancellationToken cancellationToken)
    {
        using HttpClient http = httpClients.CreateClient(HttpClientName);
        using HttpResponseMessage response = await http.SendAsync(Request(HttpMethod.Get, path, content: null), cancellationToken).ConfigureAwait(false);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await DataAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Un refus du KMS (clé inconnue, enveloppe invalide : 400 ou 404) rend <c>null</c> ; une panne lève.</summary>
    private async Task<TransitData?> TryPostAsync(string path, TransitRequest body, CancellationToken cancellationToken, bool throwOnRefusal = false)
    {
        using HttpClient http = httpClients.CreateClient(HttpClientName);
        using HttpResponseMessage response = await http.SendAsync(Request(HttpMethod.Post, path, JsonContent.Create(body, TransitJsonContext.Default.TransitRequest)), cancellationToken)
            .ConfigureAwait(false);
        if (!throwOnRefusal && response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
        {
            return null;
        }

        return await DataAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<TransitData> DataAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();
        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
        {
            return new TransitData();
        }

        TransitResponse? payload = await response.Content.ReadFromJsonAsync(TransitJsonContext.Default.TransitResponse, cancellationToken).ConfigureAwait(false);
        return payload?.Data ?? new TransitData();
    }

    private HttpRequestMessage Request(HttpMethod method, string path, HttpContent? content)
    {
        Uri address = _transit.Address ?? throw new InvalidOperationException($"{KmsOptions.SectionName}:Transit:Address est obligatoire.");
        HttpRequestMessage request = new(method, new Uri(address, $"v1/{_transit.Mount}/{path}")) { Content = content };
        request.Headers.Add("X-Vault-Token", _transit.Token);
        return request;
    }

    private string KeyName(OrganisationId organisation) => $"{_transit.KeyPrefix}{organisation.Value:N}";

    private static string Context(OrganisationId organisation) => Convert.ToBase64String(organisation.Value.ToByteArray());
}

/// <summary>Opérations du KMS sur la clé racine d'un cabinet : rotation, réenveloppement de sa KEK, destruction.</summary>
public interface ICabinetKeyVault
{
    /// <summary>Réenveloppe la KEK par la dernière version de la clé racine. <c>true</c> si l'enveloppe a changé.</summary>
    Task<bool> RewrapAsync(OrganisationId organisation, CancellationToken cancellationToken);

    /// <summary>
    /// Réenveloppe la KEK de tous les cabinets de la cellule (passe du Worker, après les rotations automatiques du KMS) :
    /// rend le nombre d'enveloppes changées.
    /// </summary>
    Task<int> RewrapAllAsync(CancellationToken cancellationToken);

    /// <summary>Nouvelle version de la clé racine, puis réenveloppement : la KEK, les DEK et les données ne changent pas.</summary>
    Task RotateAsync(OrganisationId organisation, CancellationToken cancellationToken);

    /// <summary>Effacement par destruction de clé : tous les dossiers du cabinet deviennent illisibles, sauvegardes comprises.</summary>
    Task DestroyAsync(OrganisationId organisation, CancellationToken cancellationToken);
}

internal sealed class TransitRequest
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("derived")]
    public bool? Derived { get; set; }

    [JsonPropertyName("exportable")]
    public bool? Exportable { get; set; }

    [JsonPropertyName("context")]
    public string? Context { get; set; }

    [JsonPropertyName("bits")]
    public int? Bits { get; set; }

    [JsonPropertyName("ciphertext")]
    public string? Ciphertext { get; set; }

    [JsonPropertyName("deletion_allowed")]
    public bool? DeletionAllowed { get; set; }

    [JsonPropertyName("auto_rotate_period")]
    public string? AutoRotatePeriod { get; set; }
}

internal sealed class TransitResponse
{
    [JsonPropertyName("data")]
    public TransitData? Data { get; set; }
}

internal sealed class TransitData
{
    [JsonPropertyName("plaintext")]
    public string? Plaintext { get; set; }

    [JsonPropertyName("ciphertext")]
    public string? Ciphertext { get; set; }

    [JsonPropertyName("latest_version")]
    public int LatestVersion { get; set; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TransitRequest))]
[JsonSerializable(typeof(TransitResponse))]
internal sealed partial class TransitJsonContext : JsonSerializerContext;
