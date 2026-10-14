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

    private async Task<OperationResult<T>> RunAsync<T>(
        Func<IServiceProvider, DossierId, CancellationToken, Task<OperationResult<T>>> operation,
        CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            Result initialized = await scope.ServiceProvider.GetRequiredService<ITenantInitializer>()
                .InitializeAsync(cancellationToken).ConfigureAwait(false);
            if (initialized.IsFailure)
            {
                return OperationResult<T>.Failure(initialized.Errors.ToErrorDetails());
            }

            DossierId dossier = scope.ServiceProvider.GetRequiredService<ITenantContext>().Current.DossierId;
            return await operation(scope.ServiceProvider, dossier, cancellationToken).ConfigureAwait(false);
        }
    }
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
        return services;
    }
}
