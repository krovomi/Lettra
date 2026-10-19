namespace Lettra.Calculations;

/// <summary>
/// États membres de l'Union européenne, en codes ISO 3166-1 alpha-2 (la Grèce en « GR » ; c'est le numéro de TVA qui
/// l'écrit « EL »). Une seule liste pour la conformité des pièces et la déclaration européenne de services.
/// </summary>
public static class EuropeanUnion
{
    /// <summary>Les 27 États membres, France comprise.</summary>
    public static IReadOnlyList<string> MemberStates { get; } =
    [
        "AT", "BE", "BG", "CY", "CZ", "DE", "DK", "EE", "ES", "FI", "FR", "GR", "HR", "HU", "IE", "IT", "LT", "LU", "LV",
        "MT", "NL", "PL", "PT", "RO", "SE", "SI", "SK",
    ];
}
