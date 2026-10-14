using Lettra.Contracts.Common;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Tenancy;

/// <summary>
/// Crée un dossier : sa base (fichier SQLite chiffré sur le poste, base PostgreSQL ou SQL Server sur un serveur),
/// sa clé (DEK) enveloppée par la clé du cabinet, son schéma et son estampille, puis l'inscrit au registre.
/// </summary>
public interface IDossierProvisioner
{
    Task<Result<DossierId>> CreateAsync(OrganisationId organisation, string name, CancellationToken cancellationToken);
}

/// <summary>Les dossiers actifs d'un cabinet (registre local ou base Catalog).</summary>
public interface IDossierDirectory
{
    Task<IReadOnlyList<DossierSummary>> ListAsync(OrganisationId organisation, CancellationToken cancellationToken);
}

public static class DossierErrors
{
    public const string NameRequiredCode = ErrorCodes.Tenancy.DossierNameRequired;
    public const string NameTooLongCode = ErrorCodes.Tenancy.DossierNameTooLong;
    public const string OrganisationNotFoundCode = ErrorCodes.Tenancy.OrganisationNotFound;
    public const string NoCapacityCode = ErrorCodes.Tenancy.NoCapacity;
    public const string ProvisioningFailedCode = ErrorCodes.Tenancy.ProvisioningFailed;

    public static Error NameRequired => Error.Validation(NameRequiredCode, "Name");

    public static Error NameTooLong => Error.Validation(NameTooLongCode, "Name");

    public static Error OrganisationNotFound => Error.NotFound(OrganisationNotFoundCode);

    /// <summary>Aucun serveur de la cellule n'accepte de nouveau dossier : à traiter par l'exploitation.</summary>
    public static Error NoCapacity => new(NoCapacityCode, ErrorKind.Unexpected);

    /// <summary>La base n'a pas pu être créée ; le dossier reste en état « échec » et le Worker reprend la création.</summary>
    public static Error ProvisioningFailed => new(ProvisioningFailedCode, ErrorKind.Unexpected);
}
