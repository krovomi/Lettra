using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Exports.Application.EInvoicing;
using Lettra.Exports.Application.EInvoicing.Reception;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Mêmes cas d'usage et mêmes permissions que les routes <c>/e-invoicing</c> de l'API.</summary>
internal sealed class InProcessReceivedInvoicesGateway(IServiceScopeFactory scopes) : IReceivedInvoicesGateway
{
    public Task<OperationResult<ReceivedInvoicesResponse>> GetAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetReceivedInvoicesQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<FetchReceivedInvoicesResponse>> FetchAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new FetchReceivedInvoicesCommand(dossier), ct), cancellationToken);

    public Task<OperationResult<ReceivedInvoiceResponse>> GetInvoiceAsync(Guid id, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetReceivedInvoiceQuery(dossier, id), ct), cancellationToken);

    public Task<OperationResult<ReceiptDocument>> DownloadAsync(Guid id, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, async (sp, dossier, ct) =>
        {
            OperationResult<EInvoiceFile> file = await InProcessScope.QueryAsync(sp, new DownloadReceivedInvoiceQuery(dossier, id), ct).ConfigureAwait(false);
            return file is { IsSuccess: true, Value: { } value }
                ? OperationResult<ReceiptDocument>.Success(new ReceiptDocument(value.FileName, value.ContentType, value.Content))
                : OperationResult<ReceiptDocument>.Failure(file.Errors);
        }, cancellationToken);

    public Task<OperationResult<ReceivedInvoiceResponse>> RespondAsync(Guid id, RespondToReceivedInvoiceRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new RespondToReceivedInvoiceCommand(dossier, id, request), ct), cancellationToken);

    public Task<OperationResult<DirectoryLookupResponse>> LookupDirectoryAsync(string siren, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new LookupDirectoryQuery(dossier, siren), ct), cancellationToken);
}
