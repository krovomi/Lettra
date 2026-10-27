using Lettra.Contracts.Common;
using Lettra.Domain.Common;
using Lettra.Exports.Domain.Reception;

namespace Lettra.Exports.Application.EInvoicing.Reception;

/// <summary>Une inscription à l'annuaire central de la facturation électronique.</summary>
public sealed record DirectoryEntry(string Siren, string ElectronicAddress, string PlatformName, DateOnly Since);

/// <summary>
/// Le port vers l'annuaire central de la facturation électronique, tenu par l'administration : pour chaque entreprise
/// (SIREN), son adresse de réception et la plateforme agréée qu'elle a choisie. Les plateformes s'y réfèrent pour
/// acheminer une facture ; Lettra le consulte avant d'envoyer, et pour montrer l'inscription de l'entreprise.
/// L'adapter simulé répond pour la démonstration ; l'annuaire réel s'interroge par la plateforme partenaire.
/// </summary>
/// <remarks>Échecs attendus : <c>Platform.NotConfigured</c>, <c>Platform.Unavailable</c>.</remarks>
public interface IEInvoicingDirectory
{
    /// <summary>L'inscription de ce SIREN, ou <c>null</c> s'il n'y figure pas (il ne reçoit pas de facture électronique).</summary>
    Task<Result<DirectoryEntry?>> LookupAsync(string siren, CancellationToken cancellationToken);
}

/// <summary>Aucune plateforme configurée : l'annuaire n'est pas consulté.</summary>
internal sealed class UnconfiguredDirectory : IEInvoicingDirectory
{
    public Task<Result<DirectoryEntry?>> LookupAsync(string siren, CancellationToken cancellationToken) =>
        Task.FromResult<Result<DirectoryEntry?>>(Platform.PlatformErrors.NotConfigured);
}

/// <summary>Les factures fournisseurs reçues, dans la base du dossier.</summary>
public interface IReceivedInvoiceStore
{
    /// <summary>Les plus récentes d'abord (mise à disposition), au plus <paramref name="max"/>.</summary>
    Task<IReadOnlyList<ReceivedInvoice>> ListAsync(int max, CancellationToken cancellationToken);

    Task<ReceivedInvoice?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Celles de ces références déjà enregistrées : un relevé ne les reprend pas.</summary>
    Task<IReadOnlySet<string>> KnownReferencesAsync(IReadOnlyCollection<string> references, CancellationToken cancellationToken);

    /// <summary>Le fournisseur actif du dossier qui porte chacun de ces SIREN.</summary>
    Task<IReadOnlyDictionary<string, Guid>> SuppliersBySirenAsync(IReadOnlyCollection<string> sirens, CancellationToken cancellationToken);

    /// <summary>Ajoute une facture reçue, ou les statuts ajoutés à une facture connue ; enregistré par l'unité de travail.</summary>
    Task StageAsync(ReceivedInvoice invoice, CancellationToken cancellationToken);
}

public static class ReceptionErrors
{
    public static Error NotFound => Error.NotFound(ErrorCodes.Reception.NotFound);

    public static Error CompanySirenMissing => Error.Conflict(ErrorCodes.Reception.CompanySirenMissing);

    public static Error ResponseNotAllowed => Error.Conflict(ErrorCodes.Reception.ResponseNotAllowed);

    public static Error ReasonRequired => Error.Validation(ErrorCodes.Reception.ReasonRequired, "Reason");

    public static Error ReasonTooLong => Error.Validation(ErrorCodes.Reception.ReasonTooLong, "Reason");

    public static Error PaymentAmountInvalid => Error.Validation(ErrorCodes.Reception.PaymentAmountInvalid, "Amount");

    public static Error SirenInvalid => Error.Validation(ErrorCodes.Reception.SirenInvalid, "Siren");
}
