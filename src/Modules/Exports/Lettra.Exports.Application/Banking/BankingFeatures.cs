using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Exports.Application.Banking;

/// <summary>
/// Enregistrer un encaissement client (vague 6) : montant reçu, date, mode, et son imputation sur une ou plusieurs
/// factures du client, en tout ou partie. Écriture 512 / 411 au journal de banque, TVA sur encaissements rendue
/// exigible (44574 vers 44571), entrée au journal inaltérable, lettrage des factures soldées. Depuis une ligne de
/// relevé, la ligne est rapprochée dans la même transaction.
/// </summary>
public sealed record RecordPaymentCommand(DossierId Dossier, RecordPaymentRequest Request)
    : ICommand<Result<PaymentResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureEmettre;
}

/// <summary>
/// Contre-passer un encaissement : écriture inverse au journal de banque, factures de nouveau dues, lettrage défait
/// avec trace, ligne de relevé de nouveau à rapprocher. Rien n'est supprimé.
/// </summary>
public sealed record ReversePaymentCommand(DossierId Dossier, Guid PaymentId, ReversePaymentRequest Request)
    : ICommand<Result<PaymentResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureEmettre;
}

/// <summary>Un encaissement, ses imputations, ses écritures et son code de lettrage.</summary>
public sealed record GetPaymentQuery(DossierId Dossier, Guid PaymentId) : IQuery<Result<PaymentResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Les encaissements du dossier, du plus récent au plus ancien.</summary>
public sealed record ListPaymentsQuery(
    DossierId Dossier, DateOnly? From = null, DateOnly? To = null, Guid? CustomerId = null, string? Search = null, int Page = 1, int PageSize = 50)
    : IQuery<Result<PagedResponse<PaymentSummary>>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Les factures et acomptes validés qui restent dus, d'un client ou de tout le dossier, par échéance.</summary>
public sealed record GetOpenInvoicesQuery(DossierId Dossier, Guid? CustomerId = null)
    : IQuery<Result<IReadOnlyList<OpenInvoiceResponse>>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Importer un relevé (CAMT.053, OFX ou CSV) : les opérations déjà importées sont écartées.</summary>
public sealed record ImportBankStatementCommand(DossierId Dossier, ImportBankStatementRequest Request)
    : ICommand<Result<BankImportResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureEmettre;
}

/// <summary>Démo : importer un relevé CAMT.053 fictif fabriqué à partir des factures dues du dossier.</summary>
public sealed record ImportDemoBankStatementCommand(DossierId Dossier) : ICommand<Result<BankImportResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureEmettre;
}

/// <summary>L'écran de rapprochement : opérations du relevé, propositions, compteurs, imports.</summary>
public sealed record GetReconciliationQuery(DossierId Dossier, int Max = 500) : IQuery<Result<ReconciliationResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Valider des rapprochements, un par un ou en masse : un encaissement par ligne, chacun dans sa transaction.</summary>
public sealed record ConfirmMatchesCommand(DossierId Dossier, ConfirmMatchesRequest Request)
    : ICommand<Result<ConfirmMatchesResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureEmettre;
}

/// <summary>Écarter une opération du relevé (frais, virement interne) ou la remettre à rapprocher.</summary>
public sealed record IgnoreBankLineCommand(DossierId Dossier, Guid LineId, IgnoreBankLineRequest Request)
    : ICommand<Result<BankLineResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureEmettre;
}

public sealed record GetBankingSettingsQuery(DossierId Dossier) : IQuery<Result<BankingSettingsResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Régler le seuil des écarts de règlement passés en 658 / 758.</summary>
public sealed record UpdateBankingSettingsCommand(DossierId Dossier, UpdateBankingSettingsRequest Request)
    : ICommand<Result<BankingSettingsResponse>>, IRequirePermission
{
    public Permission Permission => Permission.CatalogueGerer;
}

/// <summary>Où en est le rapprochement bancaire du dossier (pour la vue cabinet multi-dossiers).</summary>
public sealed record GetBankReconciliationStatusQuery(DossierId Dossier)
    : IQuery<Result<BankReconciliationStatusResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>La date légale des encaissements : celle de Paris, pas celle du serveur (UTC).</summary>
internal static class BankingCalendar
{
    private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Paris).DateTime);
}
