namespace Lettra.Application.Abstractions.Messaging;

/// <summary>Commande (écriture) produisant <typeparamref name="TResult"/>.</summary>
public interface ICommand<TResult>;

/// <summary>Requête (lecture) produisant <typeparamref name="TResult"/>.</summary>
public interface IQuery<TResult>;

public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

/// <summary>Point d'entrée unique des commandes, utilisé à l'identique par l'API, la gateway in-process, la synchro et l'IA.</summary>
public interface ICommandDispatcher
{
    Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken);
}

/// <summary>Point d'entrée unique des requêtes.</summary>
public interface IQueryDispatcher
{
    Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken);
}

/// <summary>Maillon suivant du pipeline (le handler, ou le comportement interne suivant).</summary>
public delegate Task<TResult> RequestHandlerDelegate<TResult>();

/// <summary>Comportement transverse enveloppant chaque commande et requête (journalisation, dossier, autorisation, validation).</summary>
public interface IPipelineBehavior<in TRequest, TResult>
{
    Task<TResult> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken);
}

/// <summary>Validateur applicatif d'une requête ; renvoie une liste vide si elle est valide.</summary>
public interface IValidator<in T>
{
    IReadOnlyList<Domain.Common.Error> Validate(T instance);
}
