using System.Globalization;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Common;
using Lettra.Domain.Common;
using Microsoft.Net.Http.Headers;

namespace Lettra.Api.Infrastructure;

/// <summary>
/// Concurrence optimiste côté HTTP (ADR-0013) : la version d'un document voyage en <c>ETag</c> fort
/// (<c>"12"</c>), revient en <c>If-Match</c> sur les écritures, et un écart donne 412 avec le document actuel
/// dans l'extension <c>current</c> du problème, pour que l'UI propose la fusion sans relire.
/// </summary>
internal static class ConcurrencyResults
{
    private const string IfMatchVersionKey = "Lettra.IfMatchVersion";

    public static string ToETag(long version) => string.Create(CultureInfo.InvariantCulture, $"\"{version}\"");

    /// <summary>Ajoute l'<c>ETag</c> du document à une réponse réussie.</summary>
    public static IResult WithETag(this IResult result, long version) => new ETagResult(result, version);

    /// <summary>
    /// Exige <c>If-Match</c> sur la route : absent, 428 ; illisible, 412 (aucune version ne peut lui correspondre).
    /// <c>If-Match: *</c> est accepté sans contrôle de version (écriture inconditionnelle explicite).
    /// </summary>
    public static RouteHandlerBuilder RequireIfMatch(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddEndpointFilter(async (context, next) =>
            {
                HttpRequest request = context.HttpContext.Request;
                string? header = request.Headers.IfMatch.ToString();
                if (string.IsNullOrWhiteSpace(header))
                {
                    return new[] { ConcurrencyErrors.PreconditionRequired }.ToProblem();
                }

                if (header.Trim() == "*")
                {
                    context.HttpContext.Items[IfMatchVersionKey] = null;
                    return await next(context).ConfigureAwait(false);
                }

                if (!EntityTagHeaderValue.TryParseList([header], out IList<EntityTagHeaderValue>? tags)
                    || tags.Count != 1
                    || tags[0].IsWeak
                    || !long.TryParse(tags[0].Tag.AsSpan().Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out long version))
                {
                    return new[] { ConcurrencyErrors.VersionMismatch }.ToProblem();
                }

                context.HttpContext.Items[IfMatchVersionKey] = version;
                return await next(context).ConfigureAwait(false);
            })
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
    }

    /// <summary>Version lue dans <c>If-Match</c> par <see cref="RequireIfMatch"/> ; <c>null</c> pour <c>*</c>.</summary>
    public static long? IfMatchVersion(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Items.TryGetValue(IfMatchVersionKey, out object? value) ? value as long? : null;
    }

    /// <summary>
    /// Échec d'une écriture. Si la version était périmée, relit le document pour le joindre au 412 ; sinon,
    /// traduction habituelle des erreurs.
    /// </summary>
    public static async Task<IResult> WriteFailureAsync<T>(
        IReadOnlyList<Error> errors, Func<Task<Result<T>>> readCurrent, Func<T, long> versionOf)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(readCurrent);
        ArgumentNullException.ThrowIfNull(versionOf);
        if (errors.Count == 0 || errors[0].Code != ConcurrencyErrors.VersionMismatchCode)
        {
            return errors.ToProblem();
        }

        Result<T> current = await readCurrent().ConfigureAwait(false);
        if (current.IsFailure)
        {
            return current.Errors.ToProblem();
        }

        Dictionary<string, object?> extensions = new(StringComparer.Ordinal)
        {
            ["code"] = ErrorCodes.Concurrency.VersionMismatch,
            ["current"] = current.Value,
        };
        return TypedResults.Problem(
                statusCode: StatusCodes.Status412PreconditionFailed,
                title: ErrorCodes.Concurrency.VersionMismatch,
                extensions: extensions)
            .WithETag(versionOf(current.Value));
    }

    private sealed class ETagResult(IResult inner, long version) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            httpContext.Response.Headers.ETag = ToETag(version);
            return inner.ExecuteAsync(httpContext);
        }
    }
}
