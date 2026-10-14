using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Catalog;

/// <summary>Un dossier déclaré dans la configuration (section <c>Lettra:Dossiers</c>).</summary>
public sealed class DossierEntry
{
    public Guid Id { get; set; }

    public Guid OrganisationId { get; set; }

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    /// <summary>Référence de secret, de la forme <c>config:NomDeChaine</c> (résolue dans <c>ConnectionStrings</c>).</summary>
    public string ConnectionSecretRef { get; set; } = string.Empty;

    public string? Schema { get; set; }

    /// <summary>Utilisateurs autorisés. Vide : tout utilisateur authentifié (poste isolé, serveur mono-cabinet de démonstration).</summary>
    public IList<Guid> Users { get; init; } = [];
}

public sealed class CatalogOptions
{
    public const string SectionName = "Lettra";

    public IList<DossierEntry> Dossiers { get; init; } = [];
}

/// <summary>Catalogue de dossiers lu dans la configuration.</summary>
internal sealed class ConfigurationTenantCatalog(IOptionsMonitor<CatalogOptions> options) : ITenantCatalog
{
    public ValueTask<TenantDescriptor?> FindAsync(DossierId dossierId, CancellationToken cancellationToken)
    {
        DossierEntry? entry = Find(dossierId);
        return ValueTask.FromResult(entry is null
            ? null
            : new TenantDescriptor(dossierId, new OrganisationId(entry.OrganisationId), entry.Provider, entry.ConnectionSecretRef, entry.Schema));
    }

    public ValueTask<bool> UserHasAccessAsync(Guid userId, DossierId dossierId, CancellationToken cancellationToken)
    {
        DossierEntry? entry = Find(dossierId);
        return ValueTask.FromResult(entry is not null && (entry.Users.Count == 0 || entry.Users.Contains(userId)));
    }

    private DossierEntry? Find(DossierId dossierId) =>
        options.CurrentValue.Dossiers.FirstOrDefault(d => d.Id == dossierId.Value);
}

/// <summary>
/// Résout <c>config:Nom</c> dans <c>ConnectionStrings:Nom</c>. Convient au poste isolé et au serveur de cabinet
/// (variables d'environnement) ; le SaaS utilisera le coffre de l'hébergeur (annexe F).
/// </summary>
internal sealed class ConfigurationSecretStore(IConfiguration configuration) : ISecretStore
{
    private const string Prefix = "config:";

    public ValueTask<string> GetConnectionStringAsync(string secretRef, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(secretRef);
        if (!secretRef.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Référence de secret non prise en charge par ce magasin.");
        }

        string name = secretRef[Prefix.Length..];
        return ValueTask.FromResult(configuration.GetConnectionString(name)
            ?? throw new InvalidOperationException($"Chaîne de connexion « {name} » absente."));
    }
}
