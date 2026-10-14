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
    private OperationResult(T? value, IReadOnlyList<ErrorDetail> errors)
    {
        Value = value;
        Errors = errors;
    }

    public T? Value { get; }

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
}
