namespace Lettra.Domain.Common;

/// <summary>
/// Résultat sans valeur. Les échecs métier sont des <b>valeurs</b>, pas des exceptions :
/// le chemin nominal ne paie pas le coût d'une exception et la signature dit tout.
/// </summary>
public readonly struct Result : IEquatable<Result>
{
    private readonly IReadOnlyList<Error>? _errors;

    private Result(IReadOnlyList<Error>? errors) => _errors = errors;

    public bool IsSuccess => _errors is null;

    public bool IsFailure => !IsSuccess;

    /// <summary>Toutes les erreurs (vide en cas de succès).</summary>
    public IReadOnlyList<Error> Errors => _errors ?? [];

    public static Result Success() => default;

    public static Result Failure(Error error) => new([error]);

    public static Result Failure(IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return errors.Count == 0
            ? throw new ArgumentException("Un échec porte au moins une erreur.", nameof(errors))
            : new Result(errors);
    }

    /// <summary>Combine plusieurs résultats : succès si tous réussissent, sinon toutes les erreurs.</summary>
    public static Result Combine(params ReadOnlySpan<Result> results)
    {
        List<Error> errors = [];
        foreach (Result result in results)
        {
            errors.AddRange(result.Errors);
        }

        return errors.Count == 0 ? Success() : Failure(errors);
    }

    public static implicit operator Result(Error error) => Failure(error);

    public bool Equals(Result other) => Errors.SequenceEqual(other.Errors);

    public override bool Equals(object? obj) => obj is Result other && Equals(other);

    public override int GetHashCode() => Errors.Count;

    public static bool operator ==(Result left, Result right) => left.Equals(right);

    public static bool operator !=(Result left, Result right) => !left.Equals(right);
}

/// <summary>Résultat porteur d'une valeur.</summary>
public readonly struct Result<TValue> : IEquatable<Result<TValue>>
{
    private readonly TValue? _value;
    private readonly IReadOnlyList<Error>? _errors;

    private Result(TValue? value, IReadOnlyList<Error>? errors)
    {
        _value = value;
        _errors = errors;
    }

    public bool IsSuccess => _errors is null;

    public bool IsFailure => !IsSuccess;

    public IReadOnlyList<Error> Errors => _errors ?? [];

    /// <summary>La valeur ; lève si le résultat est un échec (bug d'appelant, pas cas métier).</summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Lecture de la valeur d'un résultat en échec.");

    public static Result<TValue> Success(TValue value) => new(value, null);

    public static Result<TValue> Failure(Error error) => new(default, [error]);

    public static Result<TValue> Failure(IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return errors.Count == 0
            ? throw new ArgumentException("Un échec porte au moins une erreur.", nameof(errors))
            : new Result<TValue>(default, errors);
    }

    public static implicit operator Result<TValue>(TValue value) => Success(value);

    public static implicit operator Result<TValue>(Error error) => Failure(error);

    /// <summary>Repli fonctionnel : force le traitement des deux cas.</summary>
    public TResult Match<TResult>(Func<TValue, TResult> onSuccess, Func<IReadOnlyList<Error>, TResult> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess(_value!) : onFailure(Errors);
    }

    public Result<TOther> Map<TOther>(Func<TValue, TOther> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return IsSuccess ? Result<TOther>.Success(map(_value!)) : Result<TOther>.Failure(Errors);
    }

    public bool Equals(Result<TValue> other) =>
        Errors.SequenceEqual(other.Errors) && EqualityComparer<TValue?>.Default.Equals(_value, other._value);

    public override bool Equals(object? obj) => obj is Result<TValue> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_value, Errors.Count);

    public static bool operator ==(Result<TValue> left, Result<TValue> right) => left.Equals(right);

    public static bool operator !=(Result<TValue> left, Result<TValue> right) => !left.Equals(right);
}
