using Lettra.Domain.Common;

namespace Lettra.Domain.SharedKernel;

/// <summary>SIREN : 9 chiffres, clé de Luhn. Obligatoire sur les factures entre entreprises françaises (réforme 2026-2027).</summary>
public sealed record Siren
{
    private Siren(string value) => Value = value;

    public string Value { get; }

    public static Result<Siren> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return SharedKernelErrors.SirenInvalid;
        }

        string value = Luhn.Compact(raw);
        return value.Length == 9 && Luhn.AllDigits(value) && Luhn.IsValid(value)
            ? new Siren(value)
            : SharedKernelErrors.SirenInvalid;
    }

    public override string ToString() => Value;
}
