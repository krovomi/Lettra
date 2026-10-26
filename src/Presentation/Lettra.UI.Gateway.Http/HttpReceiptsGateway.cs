using System.Net.Http.Json;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpReceiptsGateway(HttpClient http) : IReceiptsGateway
{
    public Task<OperationResult<ReceiptsResponse>> GetReceiptsAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync("api/v1/bank/receipts", ct),
            ContractsJsonContext.Default.ReceiptsResponse,
            cancellationToken);

    public Task<OperationResult<ReceiptsResponse>> GetRequestedAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync("api/v1/bank/receipts/requested", ct),
            ContractsJsonContext.Default.ReceiptsResponse,
            cancellationToken);

    public Task<OperationResult<RequestReceiptsResponse>> RequestAsync(RequestReceiptsRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync("api/v1/bank/receipts/requests", request, ContractsJsonContext.Default.RequestReceiptsRequest, ct),
            ContractsJsonContext.Default.RequestReceiptsResponse,
            cancellationToken);

    public Task<OperationResult<ReceiptLineResponse>> SetNotNeededAsync(Guid lineId, bool notNeeded, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync(
                HttpCalls.Relative($"api/v1/bank/lines/{lineId}/receipt-not-needed"),
                new SetReceiptNotNeededRequest { NotNeeded = notNeeded },
                ContractsJsonContext.Default.SetReceiptNotNeededRequest,
                ct),
            ContractsJsonContext.Default.ReceiptLineResponse,
            cancellationToken);

    public Task<OperationResult<ReceiptLineResponse>> UploadAsync(Guid lineId, UploadReceiptRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PutAsJsonAsync(HttpCalls.Relative($"api/v1/bank/lines/{lineId}/receipt"), request, ContractsJsonContext.Default.UploadReceiptRequest, ct),
            ContractsJsonContext.Default.ReceiptLineResponse,
            cancellationToken);

    public Task<OperationResult<ReceiptDocument>> DownloadAsync(Guid lineId, CancellationToken cancellationToken) =>
        HttpFileDownloads.GetAsync(
            http,
            $"api/v1/bank/lines/{lineId}/receipt",
            "justificatif",
            (name, content) => new ReceiptDocument(name, "application/octet-stream", content),
            cancellationToken);
}
