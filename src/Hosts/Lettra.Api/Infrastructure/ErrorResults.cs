using Lettra.Application.Abstractions.External;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Mapping;
using Lettra.Contracts.Common;
using Lettra.Domain.Common;

namespace Lettra.Api.Infrastructure;

/// <summary>
/// Traduit les erreurs métier en réponses RFC 9457. Le serveur n'envoie jamais de texte localisé :
/// le titre est le code, et l'extension <c>code</c> (plus <c>errors</c> par champ) suffit au client.
/// </summary>
internal static class ErrorResults
{
    public static IResult ToProblem(this IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        Error first = errors[0];
        int status = first.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorKind.PreconditionFailed when first.Code == ConcurrencyErrors.PreconditionRequiredCode => StatusCodes.Status428PreconditionRequired,
            ErrorKind.PreconditionFailed => StatusCodes.Status412PreconditionFailed,
            _ when first.Code == DirectoryErrors.UnavailableCode => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };

        Dictionary<string, object?> extensions = new(StringComparer.Ordinal) { ["code"] = first.Code };
        if (errors.Any(e => e.Target is not null))
        {
            extensions["errors"] = errors.ToErrorDetails()
                .Where(e => e.Target is not null)
                .GroupBy(e => e.Target!)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Code).Distinct().ToArray());
        }

        return TypedResults.Problem(statusCode: status, title: first.Code, extensions: extensions);
    }

    public static IResult Unexpected() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: ErrorCodes.Api.Unexpected,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = ErrorCodes.Api.Unexpected });
}
