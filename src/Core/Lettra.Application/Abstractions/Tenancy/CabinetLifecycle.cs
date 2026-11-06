using Lettra.Contracts.Common;
using Lettra.Contracts.Platform;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Tenancy;

/// <summary>
/// Cycle de vie des cabinets d'un serveur (plan du mode 5, étape 2), côté catalogue : inscription avec sa cellule et sa
/// clé, départ avec délai de rétractation, relance des créations de dossiers en échec. L'administrateur de la plateforme
/// n'y voit jamais les données d'un cabinet, seulement des compteurs.
/// </summary>
public interface ICabinetLifecycle
{
    /// <summary>
    /// <c>false</c> quand aucun cabinet ne peut arriver ici : pas de catalogue (poste isolé), ou pas de KMS pour faire naître
    /// sa clé (les cabinets s'y déclarent alors dans la configuration).
    /// </summary>
    bool IsAvailable { get; }

    Task<IReadOnlyList<string>> ListCellsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PlatformCabinetResponse>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Inscrit le cabinet (ou le retrouve, à l'identique) dans sa cellule et tire sa clé : idempotent.</summary>
    Task<Result<PlatformCabinetResponse>> RegisterAsync(OrganisationId organisation, string name, string cellId, CancellationToken cancellationToken);

    /// <summary>Départ demandé : le cabinet travaille et exporte jusqu'à la fin du délai de rétractation, puis tout est effacé.</summary>
    Task<Result<PlatformCabinetResponse>> ScheduleDepartureAsync(OrganisationId organisation, Guid requestedBy, CancellationToken cancellationToken);

    Task<Result<PlatformCabinetResponse>> CancelDepartureAsync(OrganisationId organisation, CancellationToken cancellationToken);

    /// <summary>Relance les créations de dossiers en échec (serveur de bases revenu) : rend le nombre de dossiers repris.</summary>
    Task<int> RetryFailedDossiersAsync(CancellationToken cancellationToken);
}

public static class PlatformErrors
{
    public const string NameRequiredCode = ErrorCodes.CabinetLifecycle.NameRequired;
    public const string NameTooLongCode = ErrorCodes.CabinetLifecycle.NameTooLong;
    public const string UnavailableCode = ErrorCodes.CabinetLifecycle.Unavailable;
    public const string IdentityFailedCode = ErrorCodes.CabinetLifecycle.IdentityFailed;
    public const string NameTakenCode = ErrorCodes.CabinetLifecycle.NameTaken;
    public const string CellUnknownCode = ErrorCodes.CabinetLifecycle.CellUnknown;
    public const string CabinetDeclaredCode = ErrorCodes.CabinetLifecycle.CabinetDeclared;
    public const string DepartureNotCancellableCode = ErrorCodes.CabinetLifecycle.DepartureNotCancellable;
    public const string CabinetLeavingCode = ErrorCodes.CabinetLifecycle.CabinetLeaving;

    public static Error NameRequired => Error.Validation(NameRequiredCode, "Name");

    public static Error NameTooLong => Error.Validation(NameTooLongCode, "Name");

    public static Error Unavailable => Error.Conflict(UnavailableCode);

    public static Error IdentityFailed => new(IdentityFailedCode, ErrorKind.Unexpected);

    public static Error NameTaken => Error.Conflict(NameTakenCode);

    public static Error CellUnknown => Error.Validation(CellUnknownCode, "CellId");

    /// <summary>Cabinet déclaré dans la configuration du serveur : il en part en retirant sa déclaration, pas par l'API.</summary>
    public static Error CabinetDeclared => Error.Conflict(CabinetDeclaredCode);

    public static Error DepartureNotCancellable => Error.Conflict(DepartureNotCancellableCode);

    /// <summary>Le cabinet part : il n'accueille plus de nouveau dossier.</summary>
    public static Error CabinetLeaving => Error.Conflict(CabinetLeavingCode);
}
