using Lettra.Contracts.Common;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Identity;

/// <summary>Politique de séparation des tâches d'un cabinet (annexe F §3.2), conservée dans la base Catalog.</summary>
/// <param name="Enabled">Option activée par l'administrateur du cabinet.</param>
/// <param name="CreditNoteApprovalThresholdInclTax">Seuil TTC au-delà duquel un avoir attend un manager ; <c>null</c> : jamais.</param>
public sealed record SegregationOfDutiesPolicy(bool Enabled, decimal? CreditNoteApprovalThresholdInclTax)
{
    public const decimal MaxThreshold = 999_999_999.99m;

    public static SegregationOfDutiesPolicy Disabled { get; } = new(false, null);
}

/// <summary>Politiques du cabinet stockées dans la base Catalog (modes 2, 3 et 5).</summary>
public interface IOrganisationPolicyStore
{
    /// <summary>Faux sur le poste isolé (mode 1) : un seul utilisateur, la séparation des tâches n'y a pas de sens.</summary>
    bool SupportsSegregationOfDuties { get; }

    Task<SegregationOfDutiesPolicy> GetSegregationOfDutiesAsync(OrganisationId organisation, CancellationToken cancellationToken);

    Task SaveSegregationOfDutiesAsync(OrganisationId organisation, SegregationOfDutiesPolicy policy, Guid changedBy, CancellationToken cancellationToken);
}

/// <summary>Décision sur un avoir, selon la politique du cabinet et les droits de l'utilisateur courant.</summary>
public enum CreditNoteApproval
{
    /// <summary>Politique inactive, ou montant sous le seuil : l'avoir suit le circuit normal.</summary>
    NotRequired = 1,

    /// <summary>Au-delà du seuil, mais l'utilisateur courant est manager (<c>Avoir.Approuver</c>) : son action vaut approbation.</summary>
    GrantedByCurrentUser = 2,

    /// <summary>Au-delà du seuil : l'avoir attend l'approbation d'un manager.</summary>
    ManagerApprovalRequired = 3,
}

/// <summary>
/// Contrôles de séparation des tâches, appelés par les cas d'usage de facturation dans le dossier courant
/// (validation et émission d'une facture, création et approbation d'un avoir). Toujours permissifs en mode 1.
/// </summary>
public interface ISegregationOfDuties
{
    /// <summary>
    /// Échec <c>SegregationOfDuties.EnteredByIssuer</c> si la politique est active et que l'utilisateur courant
    /// fait partie de ceux qui ont saisi le document (<paramref name="enteredBy"/> : son auteur et ses modificateurs).
    /// </summary>
    Task<Result> EnsureCanIssueAsync(IReadOnlyCollection<Guid> enteredBy, CancellationToken cancellationToken);

    /// <summary>Dit si un avoir de ce montant TTC doit attendre un manager.</summary>
    Task<CreditNoteApproval> EvaluateCreditNoteAsync(decimal totalInclTax, CancellationToken cancellationToken);

    /// <summary>
    /// Avant d'approuver un avoir (cas d'usage protégé par <c>Avoir.Approuver</c>) : échec
    /// <c>SegregationOfDuties.ApproverIsCreator</c> si la politique est active et que l'approbateur en est l'auteur.
    /// </summary>
    Task<Result> EnsureCanApproveCreditNoteAsync(Guid createdBy, CancellationToken cancellationToken);
}

public static class SegregationOfDutiesErrors
{
    public const string EnteredByIssuerCode = ErrorCodes.SegregationOfDuties.EnteredByIssuer;
    public const string ManagerApprovalRequiredCode = ErrorCodes.SegregationOfDuties.ManagerApprovalRequired;
    public const string ApproverIsCreatorCode = ErrorCodes.SegregationOfDuties.ApproverIsCreator;
    public const string ThresholdInvalidCode = ErrorCodes.SegregationOfDuties.ThresholdInvalid;
    public const string NotAvailableCode = ErrorCodes.SegregationOfDuties.NotAvailable;

    public static Error EnteredByIssuer => new(EnteredByIssuerCode, ErrorKind.Forbidden);

    /// <summary>Pour un cas d'usage qui refuse au lieu de mettre l'avoir en attente.</summary>
    public static Error ManagerApprovalRequired => new(ManagerApprovalRequiredCode, ErrorKind.Forbidden);

    public static Error ApproverIsCreator => new(ApproverIsCreatorCode, ErrorKind.Forbidden);

    public static Error ThresholdInvalid => Error.Validation(ThresholdInvalidCode, "CreditNoteApprovalThresholdInclTax");

    public static Error NotAvailable => Error.Validation(NotAvailableCode);
}
