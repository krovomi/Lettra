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

    /// <summary>
    /// Crée un dossier qui garde son identifiant d'origine (import d'un paquet de réversibilité) : la chaîne anti-fraude
    /// part de cet identifiant. Refusé si un dossier du même identifiant existe ici, ou y a été effacé.
    /// </summary>
    Task<Result<DossierId>> CreateAsync(OrganisationId organisation, DossierId id, string name, CancellationToken cancellationToken);

    /// <summary>Retire un dossier dont l'import n'a pas abouti (base vide supprimée), pour qu'il puisse être importé à nouveau.</summary>
    Task DiscardAsync(DossierId dossier, CancellationToken cancellationToken);
}

/// <summary>
/// Réversibilité (plan du mode 5) : un dossier sort tout entier dans un paquet chiffré par une phrase secrète, et entre
/// sur un autre hôte (serveur du cabinet, poste, autre cellule) avec son identifiant, ses pièces, sa chaîne anti-fraude et
/// les clés qui la vérifient, réenveloppées par la clé du cabinet d'arrivée.
/// </summary>
public interface IDossierTransfer
{
    /// <summary>Le paquet du dossier, dans un fichier temporaire effacé à la fermeture du flux.</summary>
    Task<Result<DossierPackage>> ExportAsync(OrganisationId organisation, DossierId dossier, string passphrase, CancellationToken cancellationToken);

    Task<Result<DossierSummary>> ImportAsync(OrganisationId organisation, Stream package, string passphrase, CancellationToken cancellationToken);
}

/// <summary>Un paquet prêt à télécharger : l'appelant ferme le flux, ce qui efface le fichier temporaire.</summary>
public sealed record DossierPackage(Stream Content, string FileName);

/// <summary>
/// Effacement d'un dossier par son cabinet (plan du mode 5, étape 2) : le dossier est fermé aussitôt, l'effacement reste
/// annulable pendant le délai de rétractation, puis le Worker détruit sa clé (effacement cryptographique) et supprime sa base.
/// </summary>
public interface IDossierEraser
{
    Task<Result<DossierErasureResponse>> ScheduleAsync(OrganisationId organisation, DossierId dossier, Guid requestedBy, CancellationToken cancellationToken);

    Task<Result<DossierSummary>> CancelAsync(OrganisationId organisation, DossierId dossier, CancellationToken cancellationToken);

    Task<IReadOnlyList<DossierErasureResponse>> ListScheduledAsync(OrganisationId organisation, CancellationToken cancellationToken);

    /// <summary>
    /// Les dossiers du cabinet que son administrateur gère : ouverts, en cours de création, ou dont l'effacement est demandé
    /// (les dossiers déjà effacés n'y sont plus), chacun avec ce que le serveur sait en faire.
    /// </summary>
    Task<IReadOnlyList<CabinetDossierResponse>> ListAsync(OrganisationId organisation, CancellationToken cancellationToken);
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
    public const string DeclaredCode = ErrorCodes.Tenancy.DossierDeclared;
    public const string ErasureNotCancellableCode = ErrorCodes.Tenancy.ErasureNotCancellable;
    public const string NotFoundCode = ErrorCodes.Tenancy.DossierNotFound;
    public const string PassphraseTooShortCode = ErrorCodes.Tenancy.PassphraseTooShort;
    public const string PackageUnreadableCode = ErrorCodes.Tenancy.PackageUnreadable;
    public const string PackageTooNewCode = ErrorCodes.Tenancy.PackageTooNew;
    public const string AlreadyExistsCode = ErrorCodes.Tenancy.DossierAlreadyExists;
    public const string TransferUnavailableCode = ErrorCodes.Tenancy.TransferUnavailable;

    public static Error NameRequired => Error.Validation(NameRequiredCode, "Name");

    public static Error NameTooLong => Error.Validation(NameTooLongCode, "Name");

    public static Error OrganisationNotFound => Error.NotFound(OrganisationNotFoundCode);

    /// <summary>Aucun serveur de la cellule n'accepte de nouveau dossier : à traiter par l'exploitation.</summary>
    public static Error NoCapacity => new(NoCapacityCode, ErrorKind.Unexpected);

    /// <summary>La base n'a pas pu être créée ; le dossier reste en état « échec » et le Worker reprend la création.</summary>
    public static Error ProvisioningFailed => new(ProvisioningFailedCode, ErrorKind.Unexpected);

    /// <summary>Base déclarée dans la configuration du serveur (serveur du cabinet, démonstration) : elle ne s'efface pas par l'API.</summary>
    public static Error Declared => Error.Conflict(DeclaredCode);

    /// <summary>Aucun effacement à annuler, ou délai de rétractation passé : l'effacement est en cours.</summary>
    public static Error ErasureNotCancellable => Error.Conflict(ErasureNotCancellableCode);

    /// <summary>Aucun dossier de ce cabinet sous cet identifiant.</summary>
    public static Error NotFound => Error.NotFound(NotFoundCode);

    public static Error PassphraseTooShort => Error.Validation(PassphraseTooShortCode, "Passphrase");

    /// <summary>Phrase secrète incorrecte, fichier abîmé ou étranger : le chiffrement authentifié ne distingue pas les trois.</summary>
    public static Error PackageUnreadable => Error.Validation(PackageUnreadableCode, "Package");

    /// <summary>Paquet d'un schéma que cette version ne connaît pas : il faut d'abord mettre l'hôte à jour.</summary>
    public static Error PackageTooNew => Error.Conflict(PackageTooNewCode);

    public static Error AlreadyExists => Error.Conflict(AlreadyExistsCode);

    /// <summary>Hôte sans export ni import (poste relié à un serveur, qui n'a qu'une réplique du dossier).</summary>
    public static Error TransferUnavailable => Error.Conflict(TransferUnavailableCode);
}
