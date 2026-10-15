using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Data.Sqlite;

namespace Lettra.Infrastructure.Persistence.Sqlite.Local;

/// <summary>Résout <c>local:{dossier}</c> en chaîne SQLite, avec la DEK du dossier déchiffrée par la KEK du cabinet.</summary>
internal sealed class LocalSecretSource(LocalDossierRegistry registry, IDossierKeyring keyring) : ISecretSource
{
    public string Prefix => LocalDossierRegistry.SecretPrefix;

    public async ValueTask<string> ResolveAsync(string secretRef, CancellationToken cancellationToken)
    {
        Guid dossierId = Guid.Parse(secretRef.AsSpan(Prefix.Length));
        LocalDossierManifest manifest = await registry.FindAsync(dossierId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Dossier local introuvable.");
        byte[] dek = await keyring.UnwrapDekAsync(
            new OrganisationId(manifest.OrganisationId), new DossierId(manifest.DossierId), manifest.WrappedDek, cancellationToken).ConfigureAwait(false);

        // La clé n'est jamais écrite sur le disque : elle ne vit que dans la chaîne de connexion en mémoire.
        return new SqliteConnectionStringBuilder
        {
            DataSource = manifest.DatabaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Password = "x'" + Convert.ToHexString(dek) + "'",
            Pooling = true,
        }.ToString();
    }
}

/// <summary>Modes 1 et 4 : les dossiers du poste. L'utilisateur du poste a accès à tous ses dossiers.</summary>
internal sealed class LocalTenantCatalog(LocalDossierRegistry registry) : ITenantCatalog, IDossierDirectory
{
    public async ValueTask<TenantDescriptor?> FindAsync(DossierId dossierId, CancellationToken cancellationToken)
    {
        LocalDossierManifest? manifest = await registry.FindAsync(dossierId.Value, cancellationToken).ConfigureAwait(false);
        return manifest is null ? null : Describe(manifest);
    }

    public ValueTask<bool> UserHasAccessAsync(Guid userId, DossierId dossierId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(true);

    public async Task<IReadOnlyList<DossierSummary>> ListAsync(OrganisationId organisation, CancellationToken cancellationToken) =>
        [.. (await registry.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(m => m.OrganisationId == organisation.Value)
            .Select(m => new DossierSummary(m.DossierId, m.Name))];

    internal static TenantDescriptor Describe(LocalDossierManifest manifest) =>
        new(new DossierId(manifest.DossierId), new OrganisationId(manifest.OrganisationId), DatabaseProvider.Sqlite,
            LocalDossierRegistry.SecretRef(manifest.DossierId));
}

/// <summary>
/// Création d'un dossier sur le poste : DEK enveloppée par la clé du cabinet, manifeste, base chiffrée migrée et
/// estampillée. Le dossier créé devient le dossier ouvert.
/// </summary>
internal sealed class LocalDossierProvisioner(
    LocalDossierRegistry registry,
    IDossierKeyring keyring,
    TenantMigrator migrator,
    TimeProvider clock) : IDossierProvisioner
{
    public Task<Result<DossierId>> CreateAsync(OrganisationId organisation, string name, CancellationToken cancellationToken) =>
        CreateAsync(organisation, new DossierId(Guid.CreateVersion7()), name, cancellationToken);

    /// <summary>
    /// Réplique d'un dossier du serveur sur un poste relié : même identifiant que sur le serveur, pour que la
    /// synchronisation désigne le même dossier des deux côtés.
    /// </summary>
    public async Task<Result<DossierId>> CreateAsync(OrganisationId organisation, DossierId id, string name, CancellationToken cancellationToken)
    {
        byte[] wrapped = await keyring.CreateWrappedDekAsync(organisation, id, cancellationToken).ConfigureAwait(false);
        LocalDossierManifest manifest = new(id.Value, organisation.Value, name, registry.DatabasePath(id.Value), wrapped, clock.GetUtcNow());
        await registry.SaveAsync(manifest, cancellationToken).ConfigureAwait(false);

        await migrator.MigrateAsync(LocalTenantCatalog.Describe(manifest), stampIfMissing: true, cancellationToken).ConfigureAwait(false);
        await registry.SetActiveAsync(id, cancellationToken).ConfigureAwait(false);
        return id;
    }
}
