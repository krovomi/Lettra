using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;
using Lettra.Exports.Application.Banking;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Mêmes requêtes et mêmes permissions que les routes <c>/bank/receipts</c> et <c>/bank/lines/{id}/receipt</c> de l'API.</summary>
internal sealed class InProcessReceiptsGateway(IServiceScopeFactory scopes) : IReceiptsGateway
{
    public Task<OperationResult<ReceiptsResponse>> GetReceiptsAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetReceiptsQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<ReceiptsResponse>> GetRequestedAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetRequestedReceiptsQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<RequestReceiptsResponse>> RequestAsync(RequestReceiptsRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new RequestReceiptsCommand(dossier, request), ct), cancellationToken);

    public Task<OperationResult<ReceiptLineResponse>> SetNotNeededAsync(Guid lineId, bool notNeeded, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessScope.SendAsync(sp, new SetReceiptNotNeededCommand(dossier, lineId, new SetReceiptNotNeededRequest { NotNeeded = notNeeded }), ct),
            cancellationToken);

    public Task<OperationResult<ReceiptLineResponse>> UploadAsync(Guid lineId, UploadReceiptRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new UploadReceiptCommand(dossier, lineId, request), ct), cancellationToken);

    public Task<OperationResult<ReceiptDocument>> DownloadAsync(Guid lineId, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, async (sp, dossier, ct) =>
        {
            OperationResult<ReceiptFile> file = await InProcessScope.QueryAsync(sp, new GetReceiptFileQuery(dossier, lineId), ct).ConfigureAwait(false);
            return file is { IsSuccess: true, Value: { } value }
                ? OperationResult<ReceiptDocument>.Success(new ReceiptDocument(value.FileName, value.ContentType, value.Content))
                : OperationResult<ReceiptDocument>.Failure(file.Errors);
        }, cancellationToken);
}
