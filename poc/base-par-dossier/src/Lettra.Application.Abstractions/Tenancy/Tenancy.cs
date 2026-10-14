namespace Lettra.Application.Abstractions.Tenancy;

public enum DeploymentMode
{
    IsolatedDesktop = 1,   // SQLite chiffré, hors ligne
    LocalNetwork    = 2,   // client-serveur LAN
    OnPremRemote    = 3,   // PostgreSQL dédié distant
    HybridCloud     = 4,   // SQLite local + synchro cloud
    HostedSaaS      = 5    // multi-tenant cloud, cellules
}

public enum DatabaseProvider { Sqlite = 1, PostgreSql = 2, SqlServer = 3 }

/// Tout ce qu'il faut pour ouvrir la base d'un dossier. Ne contient jamais de secret :
/// la chaîne de connexion est résolue à partir de <see cref="ConnectionSecretRef"/> par <c>ISecretStore</c>.
public sealed record TenantDescriptor(
    Guid DossierId,
    Guid OrganisationId,
    DatabaseProvider Provider,
    string ConnectionSecretRef,
    string? Schema = null);

public interface ITenantContext
{
    TenantDescriptor Current { get; }
    bool IsResolved { get; }
}

public interface ITenantResolver
{
    ValueTask<TenantDescriptor> ResolveAsync(CancellationToken ct = default);
}

/// Registre des dossiers (modes 2, 3 et 5).
public interface ITenantCatalog
{
    ValueTask<TenantDescriptor?> FindAsync(Guid organisationId, Guid dossierId, CancellationToken ct = default);
    ValueTask<bool> UserHasAccessAsync(string userId, Guid dossierId, CancellationToken ct = default);
}

public class TenantNotFoundException(string message) : Exception(message);

public sealed class TenantAccessDeniedException(Guid dossierId)
    : Exception($"Accès refusé au dossier {dossierId}.")
{
    public Guid DossierId { get; } = dossierId;
}

/// La base ouverte n'appartient pas au dossier actif. Ne doit jamais arriver ; si c'est le cas,
/// la requête est bloquée avant d'avoir lu ou écrit quoi que ce soit.
public sealed class DossierMismatchException(Guid expected, Guid? actual)
    : Exception($"La base ouverte appartient au dossier {actual?.ToString() ?? "inconnu"}, attendu {expected}. Requête bloquée.")
{
    public Guid Expected { get; } = expected;
    public Guid? Actual { get; } = actual;
}
