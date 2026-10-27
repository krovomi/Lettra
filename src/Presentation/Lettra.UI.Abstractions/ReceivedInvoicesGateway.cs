using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Factures fournisseurs reçues par la plateforme agréée, et annuaire de la facturation électronique. Lire demande
/// <c>DossierLire</c> ; relever les factures et y répondre, <c>FacturesRecuesTraiter</c> ; chercher à l'annuaire,
/// <c>TiersConsulter</c>. Refus possibles : <c>Reception.*</c>, <c>Platform.NotConfigured</c>, <c>Platform.Unavailable</c>.
/// </summary>
public interface IReceivedInvoicesGateway
{
    Task<OperationResult<ReceivedInvoicesResponse>> GetAsync(CancellationToken cancellationToken);

    Task<OperationResult<FetchReceivedInvoicesResponse>> FetchAsync(CancellationToken cancellationToken);

    Task<OperationResult<ReceivedInvoiceResponse>> GetInvoiceAsync(Guid id, CancellationToken cancellationToken);

    Task<OperationResult<ReceiptDocument>> DownloadAsync(Guid id, CancellationToken cancellationToken);

    Task<OperationResult<ReceivedInvoiceResponse>> RespondAsync(Guid id, RespondToReceivedInvoiceRequest request, CancellationToken cancellationToken);

    Task<OperationResult<DirectoryLookupResponse>> LookupDirectoryAsync(string siren, CancellationToken cancellationToken);
}
