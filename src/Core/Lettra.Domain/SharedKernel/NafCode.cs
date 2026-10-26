namespace Lettra.Domain.SharedKernel;

/// <summary>
/// Code NAF (nomenclature d'activités française, dit aussi code APE) : quatre chiffres et une lettre, écrits « 47.24Z ».
/// Les deux premiers chiffres sont la division, le secteur retenu pour comparer des dossiers entre eux.
/// </summary>
public static class NafCode
{
    public const int Length = 6;

    /// <summary>« 4724z », « 47 24 Z » ou « 47.24Z » donnent « 47.24Z » ; <c>null</c> si vide ou mal formé.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string compact = string.Concat(value.Where(c => !char.IsWhiteSpace(c) && c != '.')).ToUpperInvariant();
        return compact.Length == 5 && compact[..4].All(char.IsAsciiDigit) && char.IsAsciiLetterUpper(compact[4])
            ? $"{compact[..2]}.{compact[2..]}"
            : null;
    }

    /// <summary>La division (« 47 » pour « 47.24Z ») : le secteur d'activité.</summary>
    public static string Division(string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        return code[..2];
    }
}
