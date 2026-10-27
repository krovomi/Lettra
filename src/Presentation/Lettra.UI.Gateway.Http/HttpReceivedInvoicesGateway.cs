using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpReceivedInvoicesGateway(HttpClient http) : IReceivedInvoicesGateway
{
    public Task<OperationResult<ReceivedInvoicesResponse>> GetAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync("api/v1/e-invoicing/received", ct),
            ContractsJsonContext.Default.ReceivedInvoicesResponse,
            cancellationToken);

    public Task<OperationResult<FetchReceivedInvoicesResponse>> FetchAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsync("api/v1/e-invoicing/received/fetch", null, ct),
            ContractsJsonContext.Default.FetchReceivedInvoicesResponse,
            cancellationToken);

    public Task<OperationResult<ReceivedInvoiceResponse>> GetInvoiceAsync(Guid id, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/e-invoicing/received/{id}"), ct),
            ContractsJsonContext.Default.ReceivedInvoiceResponse,
            cancellationToken);

    public Task<OperationResult<ReceiptDocument>> DownloadAsync(Guid id, CancellationToken cancellationToken) =>
        HttpFileDownloads.GetAsync(
            http,
            $"api/v1/e-invoicing/received/{id}/file",
            "facture-recue.xml",
            (name, content) => new ReceiptDocument(name, "application/xml", content),
            cancellationToken);

    public Task<OperationResult<ReceivedInvoiceResponse>> RespondAsync(Guid id, RespondToReceivedInvoiceRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync(
                HttpCalls.Relative($"api/v1/e-invoicing/received/{id}/responses"), request, ContractsJsonContext.Default.RespondToReceivedInvoiceRequest, ct),
            ContractsJsonContext.Default.ReceivedInvoiceResponse,
            cancellationToken);

    public Task<OperationResult<DirectoryLookupResponse>> LookupDirectoryAsync(string siren, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/e-invoicing/directory/{Uri.EscapeDataString(siren)}"), ct),
            ContractsJsonContext.Default.DirectoryLookupResponse,
            cancellationToken);
}
