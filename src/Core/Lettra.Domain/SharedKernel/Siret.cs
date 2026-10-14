using Lettra.Domain.Common;

namespace Lettra.Domain.SharedKernel;

/// <summary>
/// SIRET : SIREN (9 chiffres) + NIC (5 chiffres), clé de Luhn sur les 14 chiffres.
/// Exception connue : les établissements de La Poste (SIREN 356 000 000), dont la
/// somme des chiffres doit être un multiple de 5.
/// </summary>
public sealed record Siret
{
    private const string LaPosteSiren = "356000000";

    private Siret(string value) => Value = value;

    public string Value { get; }

    public Siren Siren => Siren.Create(Value[..9]).Value;

    public static Result<Siret> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return SharedKernelErrors.SiretInvalid;
        }

        string value = Luhn.Compact(raw);
        if (value.Length != 14 || !Luhn.AllDigits(value))
        {
            return SharedKernelErrors.SiretInvalid;
        }

        bool valid = value.StartsWith(LaPosteSiren, StringComparison.Ordinal)
            ? value.Sum(c => c - '0') % 5 == 0
            : Luhn.IsValid(value);

        return valid ? new Siret(value) : SharedKernelErrors.SiretInvalid;
    }

    public override string ToString() => Value;
}
