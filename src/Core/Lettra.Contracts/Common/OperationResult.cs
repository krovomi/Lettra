namespace Lettra.Contracts.Common;

/// <summary>Une erreur telle que la reçoit un client : un code à traduire, et le champ concerné s'il y en a un.</summary>
/// <param name="Code">Code de <see cref="ErrorCodes"/>.</param>
/// <param name="Target">Nom de la propriété du contrat concernée (ex. <c>Siret</c>), ou <c>null</c>.</param>
public sealed record ErrorDetail(string Code, string? Target = null);

/// <summary>
/// Résultat d'un appel vu par l'UI, identique quel que soit le transport (in-process ou HTTP).
/// </summary>
public sealed record OperationResult<T>
{
    private OperationResult(T? value, IReadOnlyList<ErrorDetail> errors, T? current = default)
    {
        Value = value;
        Errors = errors;
        Current = current;
    }

    public T? Value { get; }

    /// <summary>
    /// Sur un refus <c>Concurrency.VersionMismatch</c> : le document tel qu'il est maintenant sur le serveur, pour
    /// proposer la fusion champ par champ (ADR-0013). <c>null</c> dans tous les autres cas.
    /// </summary>
    public T? Current { get; }

    public IReadOnlyList<ErrorDetail> Errors { get; }

    public bool IsSuccess => Errors.Count == 0;

    public static OperationResult<T> Success(T value) => new(value, []);

    public static OperationResult<T> Failure(IReadOnlyList<ErrorDetail> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return errors.Count == 0
            ? throw new ArgumentException("Un échec porte au moins une erreur.", nameof(errors))
            : new(default, errors);
    }

    public static OperationResult<T> Failure(string code) => Failure([new ErrorDetail(code)]);

    /// <summary>Écriture refusée parce que le document a changé depuis sa lecture ; <paramref name="current"/> est sa version actuelle.</summary>
    public static OperationResult<T> Stale(T current) =>
        new(default, [new ErrorDetail(ErrorCodes.Concurrency.VersionMismatch)], current);

    /// <summary>Vrai si l'écriture a été refusée pour cause de version périmée.</summary>
    public bool IsStale => Errors.Any(e => e.Code == ErrorCodes.Concurrency.VersionMismatch);
}
