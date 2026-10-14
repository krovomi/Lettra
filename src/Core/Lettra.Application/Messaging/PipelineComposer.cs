using System.Reflection;
using Lettra.Application.Abstractions.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Application.Messaging;

/// <summary>
/// Compose les comportements en oignon autour du handler, pour les commandes comme pour les requêtes.
/// Le premier comportement enregistré est le plus externe.
/// </summary>
internal static class PipelineComposer
{
    public static Task<TResult> Run<TResult>(
        IServiceProvider services,
        Type requestType,
        object request,
        object handler,
        Type handlerType,
        CancellationToken cancellationToken)
    {
        MethodInfo handle = handlerType.GetMethod("HandleAsync")!;
        RequestHandlerDelegate<TResult> pipeline = () =>
            (Task<TResult>)handle.Invoke(handler, [request, cancellationToken])!;

        Type behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResult));
        MethodInfo behaviorHandle = behaviorType.GetMethod("HandleAsync")!;

        foreach (object behavior in services.GetServices(behaviorType).OfType<object>().Reverse())
        {
            RequestHandlerDelegate<TResult> next = pipeline;
            pipeline = () => (Task<TResult>)behaviorHandle.Invoke(behavior, [request, next, cancellationToken])!;
        }

        return pipeline();
    }
}

/// <summary>Dispatcher de commandes (style médiateur, sans dépendance externe).</summary>
internal sealed class CommandDispatcher(IServiceProvider services) : ICommandDispatcher
{
    public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Type type = command.GetType();
        Type handlerType = typeof(ICommandHandler<,>).MakeGenericType(type, typeof(TResult));
        return PipelineComposer.Run<TResult>(services, type, command, services.GetRequiredService(handlerType), handlerType, cancellationToken);
    }
}

/// <summary>Dispatcher de requêtes.</summary>
internal sealed class QueryDispatcher(IServiceProvider services) : IQueryDispatcher
{
    public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Type type = query.GetType();
        Type handlerType = typeof(IQueryHandler<,>).MakeGenericType(type, typeof(TResult));
        return PipelineComposer.Run<TResult>(services, type, query, services.GetRequiredService(handlerType), handlerType, cancellationToken);
    }
}
