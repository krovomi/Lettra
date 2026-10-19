using System.Collections.Concurrent;
using System.Reflection;
using Lettra.Domain.Common;

namespace Lettra.Application.Messaging;

/// <summary>
/// Construit un échec du type de retour d'un handler (<see cref="Result"/> ou <see cref="Result{TValue}"/>).
/// Permet aux comportements du pipeline de court-circuiter sans exception.
/// </summary>
internal static class ResultFactory
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> FailureMethods = new();

    public static bool IsResultType(Type type) =>
        type == typeof(Result) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>));

    public static TResult Failure<TResult>(IReadOnlyList<Error> errors)
    {
        Type type = typeof(TResult);
        if (type == typeof(Result))
        {
            return (TResult)(object)Result.Failure(errors);
        }

        if (!IsResultType(type))
        {
            throw new InvalidOperationException($"Les handlers renvoient Result ou Result<T> ; {type.Name} reçu.");
        }

        MethodInfo method = FailureMethods.GetOrAdd(type, static t =>
            t.GetMethod(nameof(Result<>.Failure), [typeof(IReadOnlyList<Error>)])!);
        return (TResult)method.Invoke(null, [errors])!;
    }

    public static TResult Failure<TResult>(Error error) => Failure<TResult>([error]);

    /// <summary>Erreurs d'un résultat, quel que soit son type (vide si succès ou type inconnu).</summary>
    public static IReadOnlyList<Error> ErrorsOf<TResult>(TResult result) => result switch
    {
        Result r => r.Errors,
        _ => ErrorsByReflection(result),
    };

    private static IReadOnlyList<Error> ErrorsByReflection<TResult>(TResult result) =>
        result is not null && IsResultType(result.GetType())
            ? (IReadOnlyList<Error>)result.GetType().GetProperty(nameof(Result.Errors))!.GetValue(result)!
            : [];
}
