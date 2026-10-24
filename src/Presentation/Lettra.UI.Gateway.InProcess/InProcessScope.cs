using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Mapping;
using Lettra.Contracts.Common;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Un appel = un scope, comme une requête HTTP : dossier résolu, puis même pipeline et même handler que l'API.</summary>
internal static class InProcessScope
{
    public static async Task<OperationResult<T>> RunAsync<T>(
        IServiceScopeFactory scopes,
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

    public static async Task<OperationResult<T>> QueryAsync<T>(IServiceProvider services, IQuery<Result<T>> query, CancellationToken cancellationToken)
    {
        Result<T> result = await services.GetRequiredService<IQueryDispatcher>().QueryAsync(query, cancellationToken).ConfigureAwait(false);
        return result.ToOperationResult();
    }

    /// <summary>
    /// Modification puis relecture, avec le même contrat que le 412 de l'API : sur un conflit de version, la fiche
    /// actuelle accompagne le refus, pour la fusion à l'écran.
    /// </summary>
    public static async Task<OperationResult<T>> WriteThenReadAsync<T>(
        IServiceProvider services, ICommand<Result> command, IQuery<Result<T>> read, CancellationToken cancellationToken)
    {
        Result written = await services.GetRequiredService<ICommandDispatcher>().SendAsync(command, cancellationToken).ConfigureAwait(false);
        if (written.IsSuccess)
        {
            return await QueryAsync(services, read, cancellationToken).ConfigureAwait(false);
        }

        if (written.Errors[0].Code == ConcurrencyErrors.VersionMismatchCode)
        {
            OperationResult<T> current = await QueryAsync(services, read, cancellationToken).ConfigureAwait(false);
            if (current is { IsSuccess: true, Value: { } value })
            {
                return OperationResult<T>.Stale(value);
            }
        }

        return OperationResult<T>.Failure(written.Errors.ToErrorDetails());
    }

    /// <summary>Création puis relecture de ce qui vient d'être créé.</summary>
    public static async Task<OperationResult<T>> CreateThenReadAsync<TId, T>(
        IServiceProvider services, ICommand<Result<TId>> command, Func<TId, IQuery<Result<T>>> read, CancellationToken cancellationToken)
    {
        Result<TId> created = await services.GetRequiredService<ICommandDispatcher>().SendAsync(command, cancellationToken).ConfigureAwait(false);
        return created.IsFailure
            ? OperationResult<T>.Failure(created.Errors.ToErrorDetails())
            : await QueryAsync(services, read(created.Value), cancellationToken).ConfigureAwait(false);
    }

    public static async Task<OperationResult<bool>> SendAsync(IServiceProvider services, ICommand<Result> command, CancellationToken cancellationToken)
    {
        Result done = await services.GetRequiredService<ICommandDispatcher>().SendAsync(command, cancellationToken).ConfigureAwait(false);
        return done.IsFailure ? OperationResult<bool>.Failure(done.Errors.ToErrorDetails()) : OperationResult<bool>.Success(true);
    }

    /// <summary>Hors dossier (cabinet, compte, session, pilotage) : un scope, sans dossier à initialiser.</summary>
    public static async Task<OperationResult<T>> OutsideDossierAsync<T>(
        IServiceScopeFactory scopes, Func<IServiceProvider, Task<OperationResult<T>>> operation)
    {
        AsyncServiceScope scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            return await operation(scope.ServiceProvider).ConfigureAwait(false);
        }
    }

    public static async Task<OperationResult<T>> SendAsync<T>(IServiceProvider services, ICommand<Result<T>> command, CancellationToken cancellationToken)
    {
        Result<T> result = await services.GetRequiredService<ICommandDispatcher>().SendAsync(command, cancellationToken).ConfigureAwait(false);
        return result.ToOperationResult();
    }
}
