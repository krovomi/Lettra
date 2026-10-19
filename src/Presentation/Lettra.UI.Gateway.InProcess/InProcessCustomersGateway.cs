using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Customers;
using Lettra.Application.Mapping;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>
/// Chaque appel ouvre un scope, comme une requête HTTP : dossier résolu, même pipeline (dossier, droits,
/// validation), même handler que l'API. Le chemin réseau et le chemin local ne peuvent pas diverger.
/// </summary>
internal sealed class InProcessCustomersGateway(IServiceScopeFactory scopes) : ICustomersGateway
{
    public Task<OperationResult<CustomerResponse>> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken) =>
        RunAsync(async (sp, dossier, ct) =>
        {
            Result<CustomerId> created = await sp.GetRequiredService<ICommandDispatcher>()
                .SendAsync(request.ToCommand(dossier), ct).ConfigureAwait(false);
            if (created.IsFailure)
            {
                return OperationResult<CustomerResponse>.Failure(created.Errors.ToErrorDetails());
            }

            Result<CustomerResponse> read = await sp.GetRequiredService<IQueryDispatcher>()
                .QueryAsync(new GetCustomerQuery(dossier, created.Value), ct).ConfigureAwait(false);
            return read.ToOperationResult();
        }, cancellationToken);

    public Task<OperationResult<PagedResponse<CustomerSummary>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        RunAsync(async (sp, dossier, ct) =>
        {
            Result<PagedResponse<CustomerSummary>> result = await sp.GetRequiredService<IQueryDispatcher>()
                .QueryAsync(new ListCustomersQuery(dossier, page, pageSize), ct).ConfigureAwait(false);
            return result.ToOperationResult();
        }, cancellationToken);

    public Task<OperationResult<CustomerResponse>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        RunAsync(async (sp, dossier, ct) =>
        {
            Result<CustomerResponse> result = await sp.GetRequiredService<IQueryDispatcher>()
                .QueryAsync(new GetCustomerQuery(dossier, new CustomerId(id)), ct).ConfigureAwait(false);
            return result.ToOperationResult();
        }, cancellationToken);

    public Task<OperationResult<PagedResponse<CustomerSummary>>> SearchAsync(string? search, int page, int pageSize, CancellationToken cancellationToken) =>
        RunAsync((sp, dossier, ct) => InProcessScope.QueryAsync(
            sp, new ListCustomersQuery(dossier, page, pageSize, string.IsNullOrWhiteSpace(search) ? null : search.Trim()), ct), cancellationToken);

    public Task<OperationResult<CustomerResponse>> UpdateAsync(Guid id, long expectedVersion, CreateCustomerRequest request, CancellationToken cancellationToken) =>
        RunAsync((sp, dossier, ct) => InProcessScope.WriteThenReadAsync(
            sp, request.ToUpdateCommand(dossier, new CustomerId(id), expectedVersion), new GetCustomerQuery(dossier, new CustomerId(id)), ct), cancellationToken);

    public Task<OperationResult<bool>> DeleteAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken) =>
        RunAsync((sp, dossier, ct) => InProcessCommands.SendAsync(sp, new DeleteCustomerCommand(dossier, new CustomerId(id), expectedVersion), ct), cancellationToken);

    private Task<OperationResult<T>> RunAsync<T>(
        Func<IServiceProvider, DossierId, CancellationToken, Task<OperationResult<T>>> operation,
        CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, operation, cancellationToken);
}

/// <summary>Le dossier demandé est celui ouvert dans l'application.</summary>
internal sealed class SelectionDossierSource(IDossierSelection selection) : IRequestedDossierSource
{
    public DossierId? RequestedDossier =>
        selection.CurrentDossierId is Guid id && id != Guid.Empty ? new DossierId(id) : null;
}

public static class DependencyInjection
{
    public static IServiceCollection AddInProcessGateways(this IServiceCollection services)
    {
        services.AddScoped<IRequestedDossierSource, SelectionDossierSource>();
        services.AddSingleton<ICustomersGateway, InProcessCustomersGateway>();
        services.AddSingleton<ISessionGateway, InProcessSessionGateway>();
        services.AddSingleton<ISuppliersGateway, InProcessSuppliersGateway>();
        services.AddSingleton<ICatalogGateway, InProcessCatalogGateway>();
        services.AddSingleton<IDirectoryGateway, InProcessDirectoryGateway>();
        services.AddSingleton<IQuotesGateway, InProcessQuotesGateway>();
        services.AddSingleton<IInvoicesGateway, InProcessInvoicesGateway>();
        services.AddSingleton<IPlatformTransmissionGateway, InProcessPlatformTransmissionGateway>();
        services.AddSingleton<IReceivablesGateway, InProcessReceivablesGateway>();
        services.AddSingleton<IFecGateway, InProcessFecGateway>();
        services.AddSingleton<IAccountingGateway, InProcessAccountingGateway>();
        services.AddSingleton<ITaxGateway, InProcessTaxGateway>();
        services.AddSingleton<IBillingSettingsGateway, InProcessBillingSettingsGateway>();
        services.AddSingleton<IPriceListsGateway, InProcessPriceListsGateway>();
        services.AddSingleton<IAlertsGateway, InProcessAlertsGateway>();
        services.AddSingleton<IAssistantGateway, InProcessAssistantGateway>();
        services.AddSingleton<ISpeechGateway, InProcessSpeechGateway>();
        services.AddSingleton<IAdministrationGateway, InProcessAdministrationGateway>();
        return services;
    }
}
