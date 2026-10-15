using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Messaging;

namespace Lettra.Application.Behaviors;

/// <summary>
/// Une écriture concurrente détectée par la persistance (la ligne a changé entre la lecture et l'écriture)
/// devient le même échec que le contrôle du handler : <c>Concurrency.VersionMismatch</c>, puis 412 côté API.
/// </summary>
internal sealed class ConcurrencyBehavior<TRequest, TResult> : IPipelineBehavior<TRequest, TResult>
    where TRequest : notnull
{
    public async Task<TResult> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        try
        {
            return await next().ConfigureAwait(false);
        }
        catch (ConcurrencyConflictException) when (ResultFactory.IsResultType(typeof(TResult)))
        {
            return ResultFactory.Failure<TResult>(ConcurrencyErrors.VersionMismatch);
        }
    }
}
