using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Tenancy;

/// <summary>Les cinq modes de déploiement. Le mode n'est qu'un choix de composition root, jamais un <c>if</c> dans le métier.</summary>
public enum DeploymentMode
{
    /// <summary>Poste isolé, SQLite chiffré, hors ligne.</summary>
    IsolatedDesktop = 1,

    /// <summary>Client-serveur sur le réseau du cabinet.</summary>
    LocalNetwork = 2,

    /// <summary>PostgreSQL dédié distant (NAS, cloud privé).</summary>
    OnPremRemote = 3,

    /// <summary>SQLite local synchronisé avec le cloud.</summary>
    HybridCloud = 4,

    /// <summary>SaaS multi-tenant, une base par dossier.</summary>
    HostedSaaS = 5,
}

public enum DatabaseProvider
{
    Sqlite = 1,
    PostgreSql = 2,
    SqlServer = 3,
}

/// <summary>Ce qu'il faut savoir pour ouvrir la base d'un dossier. La chaîne de connexion n'y figure jamais : seulement une référence de secret.</summary>
public sealed record TenantDescriptor(
    DossierId DossierId,
    OrganisationId OrganisationId,
    DatabaseProvider Provider,
    string ConnectionSecretRef,
    string? Schema = null);

/// <summary>Dossier courant de la requête (ou de la session MAUI).</summary>
public interface ITenantContext
{
    TenantDescriptor Current { get; }

    bool IsResolved { get; }
}

/// <summary>
/// Résout le dossier courant et l'installe dans <see cref="ITenantContext"/>. Appelé une fois par
/// requête HTTP (middleware) ou par opération in-process (gateway MAUI).
/// </summary>
public interface ITenantInitializer
{
    Task<Result> InitializeAsync(CancellationToken cancellationToken);
}

/// <summary>Registre des dossiers (fichier local en modes 1 et 4, base Catalog en modes 2, 3 et 5).</summary>
public interface ITenantCatalog
{
    ValueTask<TenantDescriptor?> FindAsync(DossierId dossierId, CancellationToken cancellationToken);

    ValueTask<bool> UserHasAccessAsync(Guid userId, DossierId dossierId, CancellationToken cancellationToken);
}

/// <summary>Source du dossier demandé : en-tête <c>X-Dossier</c> côté API, dossier ouvert côté poste.</summary>
public interface IRequestedDossierSource
{
    DossierId? RequestedDossier { get; }
}

/// <summary>
/// Le dossier ouvert sur un poste (modes 1 et 4), tenu par le registre local. Absent sur un serveur : le dossier y vient
/// de chaque requête.
/// </summary>
public interface IOpenedLocalDossier
{
    Guid? ActiveDossierId { get; }

    Task SetActiveAsync(DossierId dossier, CancellationToken cancellationToken);
}

/// <summary>Une requête qui s'exécute dans un dossier précis. Le pipeline vérifie qu'il s'agit bien du dossier courant.</summary>
public interface IDossierScoped
{
    DossierId Dossier { get; }
}

/// <summary>Codes d'erreur liés au dossier.</summary>
public static class TenancyErrors
{
    public const string DossierRequiredCode = "Dossier.Required";
    public const string DossierNotFoundCode = "Dossier.NotFound";
    public const string DossierMismatchCode = "Dossier.Mismatch";

    public static Error DossierRequired => Error.Validation(DossierRequiredCode);

    /// <summary>Introuvable ou inaccessible : on ne dit pas lequel, pour ne pas révéler l'existence d'un dossier.</summary>
    public static Error DossierNotFound => Error.NotFound(DossierNotFoundCode);

    public static Error DossierMismatch => new(DossierMismatchCode, ErrorKind.Forbidden);
}
