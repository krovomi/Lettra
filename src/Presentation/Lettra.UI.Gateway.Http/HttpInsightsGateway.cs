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
}
