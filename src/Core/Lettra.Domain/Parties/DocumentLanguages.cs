namespace Lettra.Domain.Parties;

/// <summary>
/// Langues dans lesquelles une pièce peut être imprimée pour un client. Les mentions légales restent en français ;
/// la langue choisie traduit le reste du document.
/// </summary>
public static class DocumentLanguages
{
    public const string French = "fr-FR";
    public const string English = "en-US";

    public static IReadOnlyList<string> Supported { get; } = [French, English];

    /// <summary>Forme canonique d'une langue prise en charge (casse ignorée), <c>null</c> sinon.</summary>
    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Supported.FirstOrDefault(l => string.Equals(l, value.Trim(), StringComparison.OrdinalIgnoreCase));
}
