using Lettra.Contracts.Common;
using Lettra.Contracts.Tax;
using Lettra.Exports.Application.Tax;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Mêmes requêtes et mêmes permissions que les routes <c>/tax</c> de l'API.</summary>
internal sealed class InProcessTaxGateway(IServiceScopeFactory scopes) : ITaxGateway
{
    public Task<OperationResult<VatReturnResponse>> GetVatReturnAsync(DateOnly date, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetVatReturnQuery(dossier, date), ct), cancellationToken);

    public Task<OperationResult<DesResponse>> GetDesAsync(int year, int month, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetDesQuery(dossier, year, month), ct), cancellationToken);

    public Task<OperationResult<DesDocument>> ExportDesAsync(int year, int month, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, async (sp, dossier, ct) =>
        {
            OperationResult<DesFile> file = await InProcessScope.QueryAsync(sp, new ExportDesQuery(dossier, year, month), ct).ConfigureAwait(false);
            return file is { IsSuccess: true, Value: { } value }
                ? OperationResult<DesDocument>.Success(new DesDocument(value.FileName, value.Content))
                : OperationResult<DesDocument>.Failure(file.Errors);
        }, cancellationToken);

    public Task<OperationResult<FiscalCalendarResponse>> GetCalendarAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetFiscalCalendarQuery(dossier, from, to), ct), cancellationToken);

    public Task<OperationResult<IReadOnlyList<FiscalDeadlineResponse>>> GetUpcomingAsync(int days, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetUpcomingFiscalDeadlinesQuery(dossier, days), ct), cancellationToken);

    public Task<OperationResult<FiscalDeadlineResponse>> MarkFilingAsync(MarkFiscalFilingRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new MarkFiscalFilingCommand(dossier, request), ct), cancellationToken);

    public Task<OperationResult<FiscalSettingsResponse>> GetSettingsAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetFiscalSettingsQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<FiscalSettingsResponse>> UpdateSettingsAsync(UpdateFiscalSettingsRequest request, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.SendAsync(sp, new UpdateFiscalSettingsCommand(dossier, request), ct), cancellationToken);
}
