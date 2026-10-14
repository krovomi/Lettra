using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Messaging;
using Lettra.Domain.Common;

namespace Lettra.Application.Behaviors;

/// <summary>Exécute les <see cref="IValidator{T}"/> applicatifs avant le handler ; les invariants métier restent dans le domaine.</summary>
internal sealed class ValidationBehavior<TRequest, TResult>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResult>
    where TRequest : notnull
{
    public Task<TResult> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        List<Error> errors = validators.SelectMany(v => v.Validate(request)).ToList();
        return errors.Count == 0
            ? next()
            : Task.FromResult(ResultFactory.Failure<TResult>(errors));
    }
}
