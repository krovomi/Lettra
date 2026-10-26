using System.Globalization;
using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpInsightsGateway(HttpClient http) : IInsightsGateway
{
    public Task<OperationResult<PaymentWeatherResponse>> GetPaymentWeatherAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.GetAsync(HttpCalls.Relative($"api/v1/insights/payment-weather"), ct), ContractsJsonContext.Default.PaymentWeatherResponse, cancellationToken);

    public Task<OperationResult<PracticeAuditResponse>> GetPracticeAuditAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/insights/practice-audit?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"), ct),
            ContractsJsonContext.Default.PracticeAuditResponse,
            cancellationToken);

    public Task<OperationResult<PracticeAuditResponse>> RunPracticeAuditAsync(
        string fileName, byte[] content, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        Uri uri = from is { } start && to is { } end
            ? HttpCalls.Relative($"api/v1/insights/practice-audit?fileName={Uri.EscapeDataString(fileName)}&from={start:yyyy-MM-dd}&to={end:yyyy-MM-dd}")
            : HttpCalls.Relative($"api/v1/insights/practice-audit?fileName={Uri.EscapeDataString(fileName)}");
        return HttpCalls.SendAsync(
            async ct =>
            {
                using ByteArrayContent body = new(content);
                return await http.PostAsync(uri, body, ct).ConfigureAwait(false);
            },
            ContractsJsonContext.Default.PracticeAuditResponse,
            cancellationToken);
    }

    public Task<OperationResult<InvoiceProofResponse>> GetInvoiceProofAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.GetAsync(HttpCalls.Relative($"api/v1/insights/invoice-proof/{invoiceId}"), ct), ContractsJsonContext.Default.InvoiceProofResponse, cancellationToken);

    public Task<OperationResult<InvoiceProofCheckResponse>> VerifyInvoiceProofAsync(string code, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync(
                HttpCalls.Relative($"api/v1/insights/invoice-proof/verification"), new VerifyInvoiceProofRequest { Code = code }, ContractsJsonContext.Default.VerifyInvoiceProofRequest, ct),
            ContractsJsonContext.Default.InvoiceProofCheckResponse,
            cancellationToken);

    public Task<OperationResult<IReadOnlyList<InvoiceProofKeyResponse>>> ListInvoiceProofKeysAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/insights/invoice-proof/keys"), ct), ContractsJsonContext.Default.IReadOnlyListInvoiceProofKeyResponse, cancellationToken);

    public Task<OperationResult<DossierAsOfResponse>> GetDossierAsOfAsync(DateTimeOffset at, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/insights/as-of?at={Uri.EscapeDataString(at.ToString("O", CultureInfo.InvariantCulture))}"), ct),
            ContractsJsonContext.Default.DossierAsOfResponse,
            cancellationToken);

    public Task<OperationResult<FigureExplanationResponse>> ExplainFigureAsync(ExplainedFigureDto figure, DateOnly? month, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(month is { } m
                ? HttpCalls.Relative($"api/v1/insights/why?figure={figure}&month={m.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}")
                : HttpCalls.Relative($"api/v1/insights/why?figure={figure}"), ct),
            ContractsJsonContext.Default.FigureExplanationResponse,
            cancellationToken);

    public Task<OperationResult<WhatIfResponse>> SimulateWhatIfAsync(WhatIfRequest scenario, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync(HttpCalls.Relative($"api/v1/insights/what-if"), scenario, ContractsJsonContext.Default.WhatIfRequest, ct),
            ContractsJsonContext.Default.WhatIfResponse,
            cancellationToken);
}
