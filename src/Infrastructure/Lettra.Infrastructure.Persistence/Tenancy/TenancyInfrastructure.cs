using Lettra.Application.Abstractions.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Persistence.Tenancy;

/// <summary>
/// Résout une référence de secret (<c>ConnectionSecretRef</c>) en chaîne de connexion. Jamais de secret en clair
/// dans le catalogue : seulement une référence, résolue par le magasin du mode (voir <see cref="SecretStoreRouter"/>).
/// </summary>
public interface ISecretStore
{
    ValueTask<string> GetConnectionStringAsync(string secretRef, CancellationToken cancellationToken);
}

/// <summary>Un magasin de secrets qui sait résoudre les références commençant par <see cref="Prefix"/>.</summary>
public interface ISecretSource
{
    /// <summary>Préfixe des références prises en charge, par exemple <c>config:</c>, <c>local:</c>, <c>cluster:</c>.</summary>
    string Prefix { get; }

    ValueTask<string> ResolveAsync(string secretRef, CancellationToken cancellationToken);
}

/// <summary>Aiguille chaque référence vers la source qui porte son préfixe (le plus long l'emporte).</summary>
internal sealed class SecretStoreRouter(IEnumerable<ISecretSource> sources) : ISecretStore
{
    private readonly ISecretSource[] _sources = [.. sources.OrderByDescending(s => s.Prefix.Length)];

    public ValueTask<string> GetConnectionStringAsync(string secretRef, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(secretRef);
        foreach (ISecretSource source in _sources)
        {
            if (secretRef.StartsWith(source.Prefix, StringComparison.Ordinal))
            {
                return source.ResolveAsync(secretRef, cancellationToken);
            }
        }

        // La référence elle-même n'est pas journalisée : elle peut nommer un serveur.
        throw new InvalidOperationException("Référence de secret non prise en charge dans ce mode de déploiement.");
    }
}

/// <summary>Stratégie par provider : configure EF pour SQLite, PostgreSQL ou SQL Server.</summary>
public interface IDbProviderConfigurator
{
    DatabaseProvider Provider { get; }

    /// <summary>Une chaîne de connexion factice, valide syntaxiquement, pour lire le modèle sans se connecter.</summary>
    string ProbeConnectionString { get; }

    void Configure(DbContextOptionsBuilder builder, string connectionString, TenantDescriptor tenant);
}

/// <summary>Opérations propres au provider qui sortent d'EF : créer ou supprimer la base d'un dossier.</summary>
public interface IDatabaseAdministrator
{
    DatabaseProvider Provider { get; }

    Task CreateDatabaseAsync(string adminConnectionString, CancellationToken cancellationToken);

    Task DropDatabaseAsync(string adminConnectionString, CancellationToken cancellationToken);
}

/// <summary>Le rôle de connexion propre à un cabinet sur un serveur de bases, et son mot de passe.</summary>
public sealed record DatabaseOwner(string Role, string Password);

/// <summary>
/// Rôles de connexion par cabinet (PostgreSQL) : chaque base de dossier appartient au rôle de son cabinet et n'accepte
/// que lui ; une erreur d'aiguillage ne peut donc pas ouvrir la base d'un autre cabinet.
/// </summary>
public interface ICabinetRoleAdministrator
{
    DatabaseProvider Provider { get; }

    /// <summary>
    /// Crée le rôle (ou met à jour son mot de passe), lui donne la base et ses objets, puis ferme la base à tout autre
    /// rôle. Idempotent : sert aussi aux bases créées avant les rôles par cabinet.
    /// </summary>
    Task AssignOwnerAsync(string adminConnectionString, DatabaseOwner owner, CancellationToken cancellationToken);

    /// <summary>Supprime le rôle d'un cabinet parti, une fois ses bases supprimées. Sans effet s'il n'existe pas.</summary>
    Task DropOwnerAsync(string adminConnectionString, string role, CancellationToken cancellationToken);
}

/// <summary>Dossier courant (scoped). Installé par <see cref="TenantInitializer"/>.</summary>
public sealed class TenantContext : ITenantContext
{
    private TenantDescriptor? _current;

    public TenantDescriptor Current =>
        _current ?? throw new InvalidOperationException("Aucun dossier actif.");

    public bool IsResolved => _current is not null;

    internal void Set(TenantDescriptor descriptor)
    {
        if (_current is not null && _current.DossierId != descriptor.DossierId)
        {
            throw new InvalidOperationException("Le dossier actif ne change pas au milieu d'une portée.");
        }

        _current = descriptor;
    }
}
