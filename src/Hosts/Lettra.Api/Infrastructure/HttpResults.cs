using Lettra.Domain.Common;

namespace Lettra.Api.Infrastructure;

/// <summary>
/// Traduit un <see cref="Result"/> en réponse HTTP : le succès porte la valeur, l'échec devient un problème RFC 9457
/// (<see cref="ErrorResults.ToProblem"/>). Les endpoints l'importent par <c>using static</c> et ne réécrivent pas ce choix.
/// </summary>
internal static class HttpResults
{
    /// <summary>200 avec la valeur, ou le problème.</summary>
    public static IResult Ok<T>(Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();

    /// <summary>204, ou le problème.</summary>
    public static IResult NoContent(Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : result.Errors.ToProblem();

    /// <summary>200 avec la valeur et son <c>ETag</c>, ou le problème.</summary>
    public static IResult Versioned<T>(Result<T> result, Func<T, long> version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return result.IsSuccess ? TypedResults.Ok(result.Value).WithETag(version(result.Value)) : result.Errors.ToProblem();
    }

    /// <summary>201 avec l'adresse de la ressource créée, ou le problème.</summary>
    public static IResult Created<T>(Func<T, string> location, Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(location);
        return result.IsSuccess ? TypedResults.Created(location(result.Value), result.Value) : result.Errors.ToProblem();
    }

    /// <summary>201 avec l'adresse et l'<c>ETag</c> de la ressource créée, ou le problème.</summary>
    public static IResult Created<T>(string location, Result<T> result, Func<T, long> version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return result.IsSuccess ? TypedResults.Created(location, result.Value).WithETag(version(result.Value)) : result.Errors.ToProblem();
    }
}
