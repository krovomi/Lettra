using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;

namespace Lettra.UI.Abstractions;

/// <summary>Une pièce déposée, téléchargée : nom, type et contenu.</summary>
public sealed record ReceiptDocument(string FileName, string ContentType, byte[] Content);

/// <summary>
/// Pièces justificatives des dépenses du relevé. Le cabinet les voit (<c>DossierLire</c>), les réclame ou les dit non
/// attendues (<c>FactureEmettre</c>) ; le client voit celles qu'on attend de lui et les dépose (<c>PiecesDeposer</c>).
/// Refus possibles : <c>Receipt.*</c>, <c>BankStatement.LineNotFound</c>.
/// </summary>
public interface IReceiptsGateway
{
    Task<OperationResult<ReceiptsResponse>> GetReceiptsAsync(CancellationToken cancellationToken);

    /// <summary>Les pièces réclamées et pas encore déposées : l'écran « Pièces à fournir » du client.</summary>
    Task<OperationResult<ReceiptsResponse>> GetRequestedAsync(CancellationToken cancellationToken);

    Task<OperationResult<RequestReceiptsResponse>> RequestAsync(RequestReceiptsRequest request, CancellationToken cancellationToken);

    Task<OperationResult<ReceiptLineResponse>> SetNotNeededAsync(Guid lineId, bool notNeeded, CancellationToken cancellationToken);

    Task<OperationResult<ReceiptLineResponse>> UploadAsync(Guid lineId, UploadReceiptRequest request, CancellationToken cancellationToken);

    Task<OperationResult<ReceiptDocument>> DownloadAsync(Guid lineId, CancellationToken cancellationToken);
}
