using System.Globalization;
using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.Contracts.Tax;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpTaxGateway(HttpClient http) : ITaxGateway
{
    public Task<OperationResult<VatReturnResponse>> GetVatReturnAsync(DateOnly date, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/tax/vat-return?date={Day(date)}"), ct),
            ContractsJsonContext.Default.VatReturnResponse,
            cancellationToken);

    public Task<OperationResult<DesResponse>> GetDesAsync(int year, int month, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/tax/des?year={year}&month={month}"), ct),
            ContractsJsonContext.Default.DesResponse,
            cancellationToken);

    public async Task<OperationResult<DesDocument>> ExportDesAsync(int year, int month, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await http.GetAsync(HttpCalls.Relative($"api/v1/tax/des/file?year={year}&month={month}"), cancellationToken)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                string? fileName = response.Content.Headers.ContentDisposition is { } disposition
                    ? (disposition.FileNameStar ?? disposition.FileName)?.Trim('"')
                    : null;
                byte[] content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                return OperationResult<DesDocument>.Success(new DesDocument(fileName ?? "DES.csv", content));
            }

            OperationResult<ProblemResponse> problem = await response.ToOperationResultAsync(ContractsJsonContext.Default.ProblemResponse, cancellationToken)
                .ConfigureAwait(false);
            return OperationResult<DesDocument>.Failure(problem.Errors);
        }
        catch (HttpRequestException)
        {
            return OperationResult<DesDocument>.Failure(ErrorCodes.Api.Unreachable);
        }
    }

    public Task<OperationResult<FiscalCalendarResponse>> GetCalendarAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/tax/calendar?from={Day(from)}&to={Day(to)}"), ct),
            ContractsJsonContext.Default.FiscalCalendarResponse,
            cancellationToken);

    public Task<OperationResult<IReadOnlyList<FiscalDeadlineResponse>>> GetUpcomingAsync(int days, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/tax/calendar/upcoming?days={days}"), ct),
            ContractsJsonContext.Default.IReadOnlyListFiscalDeadlineResponse,
            cancellationToken);

    public Task<OperationResult<FiscalDeadlineResponse>> MarkFilingAsync(MarkFiscalFilingRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PutAsJsonAsync(HttpCalls.Relative($"api/v1/tax/calendar/filings"), request, ContractsJsonContext.Default.MarkFiscalFilingRequest, ct),
            ContractsJsonContext.Default.FiscalDeadlineResponse,
            cancellationToken);

    public Task<OperationResult<FiscalSettingsResponse>> GetSettingsAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/tax/settings"), ct),
            ContractsJsonContext.Default.FiscalSettingsResponse,
            cancellationToken);

    public Task<OperationResult<FiscalSettingsResponse>> UpdateSettingsAsync(UpdateFiscalSettingsRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PutAsJsonAsync(HttpCalls.Relative($"api/v1/tax/settings"), request, ContractsJsonContext.Default.UpdateFiscalSettingsRequest, ct),
            ContractsJsonContext.Default.FiscalSettingsResponse,
            cancellationToken);

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
