using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Pricing;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Critères des listes de devis et de factures. <paramref name="Search"/> porte sur le numéro et le nom du client ;
/// les dates bornent la date d'émission.
/// </summary>
public sealed record BillingListQuery(
    string? Search = null,
    Guid? CustomerId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    int Page = 1,
    int PageSize = 50);

/// <summary>
/// Écrans Devis. <c>expectedVersion</c> est la <c>Version</c> lue : l'écriture échoue avec
/// <c>Concurrency.VersionMismatch</c> si quelqu'un a modifié le devis entre-temps (HTTP : <c>If-Match</c>, 412).
/// </summary>
public interface IQuotesGateway
{
    Task<OperationResult<PagedResponse<QuoteSummary>>> ListAsync(BillingListQuery query, QuoteStatusDto? status, CancellationToken cancellationToken);

    Task<OperationResult<QuoteResponse>> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<OperationResult<QuoteResponse>> CreateAsync(SaveQuoteRequest request, CancellationToken cancellationToken);

    Task<OperationResult<QuoteResponse>> UpdateAsync(Guid id, SaveQuoteRequest request, long? expectedVersion, CancellationToken cancellationToken);

    Task<OperationResult<bool>> DeleteAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken);

    /// <summary>Checklist de conformité du brouillon, recalculée par le serveur.</summary>
    Task<OperationResult<ComplianceReportResponse>> CheckAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Numérote (série DEV) et fige le devis.</summary>
    Task<OperationResult<QuoteResponse>> ValidateAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken);

    Task<OperationResult<QuoteResponse>> SetOutcomeAsync(Guid id, bool accepted, long? expectedVersion, CancellationToken cancellationToken);

    /// <summary>Crée une facture d'acompte brouillon sur un devis accepté.</summary>
    Task<OperationResult<InvoiceResponse>> CreateDownPaymentAsync(Guid id, CreateDownPaymentRequest request, CancellationToken cancellationToken);

    /// <summary>Crée la facture finale brouillon : les lignes du devis, moins les acomptes validés.</summary>
    Task<OperationResult<InvoiceResponse>> CreateInvoiceAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>Écrans Factures et Avoirs. Même règle de version que <see cref="IQuotesGateway"/>.</summary>
public interface IInvoicesGateway
{
    Task<OperationResult<PagedResponse<InvoiceSummary>>> ListAsync(BillingListQuery query, InvoiceKindDto? kind, InvoiceStatusDto? status, CancellationToken cancellationToken);

    Task<OperationResult<InvoiceResponse>> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<OperationResult<InvoiceResponse>> CreateAsync(SaveInvoiceRequest request, CancellationToken cancellationToken);

    Task<OperationResult<InvoiceResponse>> UpdateAsync(Guid id, SaveInvoiceRequest request, long? expectedVersion, CancellationToken cancellationToken);

    Task<OperationResult<bool>> DeleteAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken);

    Task<OperationResult<ComplianceReportResponse>> CheckAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Numérote (série FAC), fige et inscrit la pièce ; refusé tant que la checklist a une erreur.</summary>
    Task<OperationResult<InvoiceResponse>> ValidateAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken);

    /// <summary>Marque la pièce validée comme émise (envoyée au client).</summary>
    Task<OperationResult<InvoiceResponse>> IssueAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken);

    /// <summary>Crée un avoir brouillon sur une facture validée.</summary>
    Task<OperationResult<InvoiceResponse>> CreateCreditNoteAsync(Guid id, CreateCreditNoteRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Approbation d'un avoir brouillon par un manager (permission <c>Avoir.Approuver</c>), exigée par la séparation des
    /// tâches du cabinet au-delà de son seuil ; l'auteur de l'avoir ne peut pas l'approuver.
    /// </summary>
    Task<OperationResult<InvoiceResponse>> ApproveCreditAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Facture électronique EN 16931 d'une pièce validée (XML UTF-8) : CII, que l'écran joint au PDF pour en faire un
    /// Factur-X, ou UBL. Un brouillon est refusé avec <c>EInvoice.NotValidated</c>.
    /// </summary>
    Task<OperationResult<byte[]>> GetEInvoiceAsync(Guid id, EInvoiceFormatDto format, CancellationToken cancellationToken);
}

/// <summary>Listes fermées de la facturation et fiche de l'entreprise émettrice.</summary>
public interface IBillingSettingsGateway
{
    Task<OperationResult<BillingReferenceData>> GetReferenceDataAsync(CancellationToken cancellationToken);

    /// <summary>La fiche, ou <c>CompanyProfile.NotFound</c> tant qu'elle n'a pas été remplie.</summary>
    Task<OperationResult<CompanyProfileResponse>> GetCompanyProfileAsync(CancellationToken cancellationToken);

    Task<OperationResult<CompanyProfileResponse>> SaveCompanyProfileAsync(SaveCompanyProfileRequest request, long? expectedVersion, CancellationToken cancellationToken);
}

/// <summary>Grilles tarifaires par client, et prix proposés à la saisie d'une ligne.</summary>
public interface IPriceListsGateway
{
    Task<OperationResult<PagedResponse<PriceListSummary>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<OperationResult<PriceListResponse>> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<OperationResult<PriceListResponse>> CreateAsync(SavePriceListRequest request, CancellationToken cancellationToken);

    Task<OperationResult<PriceListResponse>> UpdateAsync(Guid id, SavePriceListRequest request, long? expectedVersion, CancellationToken cancellationToken);

    Task<OperationResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Prix à copier dans les lignes d'un document pour ce client.</summary>
    Task<OperationResult<IReadOnlyList<ResolvedPrice>>> ResolveAsync(Guid customerId, IReadOnlyCollection<Guid> catalogItemIds, CancellationToken cancellationToken);
}
