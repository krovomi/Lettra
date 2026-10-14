using Lettra.Contracts.Common;
using Lettra.Domain.Common;

namespace Lettra.Application.Abstractions.Persistence;

/// <summary>
/// Numérotation chronologique continue des pièces (ADR-0007) : ni trou, ni doublon, même sous concurrence.
/// </summary>
public interface IDocumentNumbering
{
    /// <summary>
    /// Réserve le numéro suivant de la série puis exécute <paramref name="work"/> avec ce numéro, et enregistre
    /// le tout dans une seule transaction. Si <paramref name="work"/> échoue, rien n'est enregistré et le numéro
    /// n'est pas consommé. <paramref name="work"/> peut être rejoué : il doit recharger ce qu'il modifie.
    /// </summary>
    Task<Result<T>> ExecuteWithNextNumberAsync<T>(
        string seriesCode,
        Func<string, CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken);
}

/// <summary>Les séries créées avec chaque dossier.</summary>
public static class NumberSeriesCodes
{
    public const string Invoices = "FAC";
    public const string Quotes = "DEV";
}

public static class NumberingErrors
{
    public const string SeriesNotFoundCode = ErrorCodes.Numbering.SeriesNotFound;
    public const string ContentionCode = ErrorCodes.Numbering.Contention;

    public static Error SeriesNotFound => Error.NotFound(SeriesNotFoundCode);

    /// <summary>La série est trop disputée : la pièce n'est pas validée, l'utilisateur peut réessayer.</summary>
    public static Error Contention => new(ContentionCode, ErrorKind.Conflict);
}
