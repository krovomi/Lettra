using Lettra.Calculations;

namespace Lettra.Domain.SharedKernel;

/// <summary>Montant dans une devise. Les opérations entre devises différentes sont interdites.</summary>
public readonly record struct Money(decimal Amount, string Currency)
{
    public const string Euro = "EUR";

    public static Money Zero(string currency = Euro) => new(0m, currency);

    public static Money Eur(decimal amount) => new(amount, Euro);

    /// <summary>Montant arrondi au centime selon la règle unique de <see cref="Rounding"/>.</summary>
    public Money RoundedToCents() => this with { Amount = Rounding.ToCents(Amount) };

    public static Money operator +(Money left, Money right) => new(left.Amount + EnsureSameCurrency(left, right).Amount, left.Currency);

    public static Money operator -(Money left, Money right) => new(left.Amount - EnsureSameCurrency(left, right).Amount, left.Currency);

    public static Money Add(Money left, Money right) => left + right;

    public static Money Subtract(Money left, Money right) => left - right;

    private static Money EnsureSameCurrency(Money left, Money right) =>
        string.Equals(left.Currency, right.Currency, StringComparison.Ordinal)
            ? right
            : throw new InvalidOperationException($"{SharedKernelErrors.CurrencyMismatchCode}: {left.Currency} ≠ {right.Currency}");
}
