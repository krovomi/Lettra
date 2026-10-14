using System.Text.Json;
using Lettra.Application.Abstractions.Security;
using Lettra.Application.Abstractions.Tenancy;
using Microsoft.Data.Sqlite;

namespace Lettra.Infrastructure.Persistence.Sqlite.Local;

/// Manifeste d'un dossier local (modes 1 et 4), posé à côté du fichier .db : identité et DEK enveloppée.
/// Sans la KEK du cabinet (coffre de l'OS), le manifeste ne permet pas d'ouvrir la base.
public sealed record LocalDossierManifest(Guid DossierId, Guid OrganisationId, string Name, string DatabaseFile, byte[] WrappedDek);

public interface ILocalDossierStore
{
    ValueTask<LocalDossierManifest?> GetActiveDossierAsync(CancellationToken ct = default);
    ValueTask<LocalDossierManifest?> FindAsync(Guid dossierId, CancellationToken ct = default);
    ValueTask<IReadOnlyList<LocalDossierManifest>> ListAsync(CancellationToken ct = default);
    ValueTask SaveAsync(LocalDossierManifest manifest, CancellationToken ct = default);
    ValueTask SetActiveAsync(Guid dossierId, CancellationToken ct = default);
    ValueTask DeleteKeyAsync(Guid dossierId, CancellationToken ct = default);
}

/// Un répertoire de données par poste : {id}.db + {id}.lettra (manifeste) + active.txt.
public sealed class LocalDossierStore(string dataDirectory) : ILocalDossierStore
{
    public const string SecretPrefix = "local:";

    public string DataDirectory => dataDirectory;

    public static string SecretRef(Guid dossierId) => $"{SecretPrefix}{dossierId:N}";

    public async ValueTask<LocalDossierManifest?> GetActiveDossierAsync(CancellationToken ct = default)
    {
        var active = Path.Combine(dataDirectory, "active.txt");
        if (!File.Exists(active)) return null;
        return await FindAsync(Guid.Parse(await File.ReadAllTextAsync(active, ct)), ct);
    }

    public async ValueTask<LocalDossierManifest?> FindAsync(Guid dossierId, CancellationToken ct = default)
    {
        var path = ManifestPath(dossierId);
        if (!File.Exists(path)) return null;
        await using var f = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<LocalDossierManifest>(f, cancellationToken: ct);
    }

    public async ValueTask<IReadOnlyList<LocalDossierManifest>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(dataDirectory)) return [];
        var list = new List<LocalDossierManifest>();
        foreach (var file in Directory.EnumerateFiles(dataDirectory, "*.lettra"))
            if (await FindAsync(Guid.Parse(Path.GetFileNameWithoutExtension(file)), ct) is { } m)
                list.Add(m);
        return list;
    }

    public async ValueTask SaveAsync(LocalDossierManifest manifest, CancellationToken ct = default)
    {
        Directory.CreateDirectory(dataDirectory);
        await using var f = File.Create(ManifestPath(manifest.DossierId));
        await JsonSerializer.SerializeAsync(f, manifest, cancellationToken: ct);
    }

    public async ValueTask SetActiveAsync(Guid dossierId, CancellationToken ct = default) =>
        await File.WriteAllTextAsync(Path.Combine(dataDirectory, "active.txt"), dossierId.ToString(), ct);

    /// Effacement par destruction de clé (ADR-0011) : la base reste sur disque mais devient illisible.
    public async ValueTask DeleteKeyAsync(Guid dossierId, CancellationToken ct = default)
    {
        if (await FindAsync(dossierId, ct) is { } m)
            await SaveAsync(m with { WrappedDek = [] }, ct);
    }

    public string DatabasePath(Guid dossierId) => Path.Combine(dataDirectory, $"{dossierId:N}.db");

    private string ManifestPath(Guid dossierId) => Path.Combine(dataDirectory, $"{dossierId:N}.lettra");
}

/// Résout <c>local:{dossier}</c> en chaîne SQLite avec la DEK déchiffrée par la KEK du cabinet.
public sealed class LocalSqliteSecretStore(ILocalDossierStore store, IDossierKeyring keyring) : ISecretStore
{
    public async ValueTask<string> GetConnectionStringAsync(string secretRef, CancellationToken ct = default)
    {
        var dossierId = Guid.Parse(secretRef[LocalDossierStore.SecretPrefix.Length..]);
        var manifest = await store.FindAsync(dossierId, ct)
                       ?? throw new TenantNotFoundException($"Dossier local {dossierId} introuvable.");
        var dek = await keyring.UnwrapDekAsync(manifest.OrganisationId, manifest.DossierId, manifest.WrappedDek, ct);

        return new SqliteConnectionStringBuilder
        {
            DataSource = manifest.DatabaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Password = Convert.ToHexString(dek),
            Pooling = true,
        }.ToString();
    }
}

/// Modes 1 et 4 : le dossier ouvert sur le poste.
public sealed class LocalTenantResolver(ILocalDossierStore store) : ITenantResolver
{
    public async ValueTask<TenantDescriptor> ResolveAsync(CancellationToken ct = default)
    {
        var dossier = await store.GetActiveDossierAsync(ct)
                      ?? throw new TenantNotFoundException("Aucun dossier ouvert sur ce poste.");
        return new TenantDescriptor(dossier.DossierId, dossier.OrganisationId, DatabaseProvider.Sqlite, LocalDossierStore.SecretRef(dossier.DossierId));
    }
}

/// Création d'un dossier sur le poste : DEK enveloppée, manifeste, base chiffrée migrée et estampillée.
public sealed class LocalDossierProvisioner(LocalDossierStore store, IDossierKeyring keyring, Tenancy.TenantMigrator migrator)
{
    public async Task<TenantDescriptor> CreateAsync(Guid organisationId, string name, CancellationToken ct = default)
    {
        var id = Guid.CreateVersion7();
        var wrapped = await keyring.CreateWrappedDekAsync(organisationId, id, ct);
        await store.SaveAsync(new LocalDossierManifest(id, organisationId, name, store.DatabasePath(id), wrapped), ct);

        var tenant = new TenantDescriptor(id, organisationId, DatabaseProvider.Sqlite, LocalDossierStore.SecretRef(id));
        await migrator.MigrateAsync(tenant, stampIdentityIfMissing: true, ct);
        return tenant;
    }

    /// Au démarrage du poste : met à jour le schéma de tous les dossiers locaux.
    public async Task<int> MigrateAllAsync(CancellationToken ct = default)
    {
        var n = 0;
        foreach (var m in await store.ListAsync(ct))
        {
            var r = await migrator.MigrateAsync(new TenantDescriptor(m.DossierId, m.OrganisationId, DatabaseProvider.Sqlite, LocalDossierStore.SecretRef(m.DossierId)), false, ct);
            n += r.Applied.Count > 0 ? 1 : 0;
        }
        return n;
    }
}
