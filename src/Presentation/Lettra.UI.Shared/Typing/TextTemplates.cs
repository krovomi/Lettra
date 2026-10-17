namespace Lettra.UI.Shared.Typing;

/// <summary>Champ de texte libre qui reçoit des modèles.</summary>
public enum TextFieldKind
{
    /// <summary>Texte quelconque : aucun modèle proposé, seulement le nettoyage et la correction orthographique.</summary>
    Free,

    Subject,
    QuoteNotes,
    InvoiceNotes,
    PaymentTerms,
}

/// <summary>Un modèle de texte : titre et corps traduits (<c>Template.&lt;id&gt;.Title</c> et <c>.Body</c>), champs où il s'insère.</summary>
public sealed record TextTemplate(string Id, IReadOnlyList<TextFieldKind> Fields)
{
    public string TitleKey => $"Template.{Id}.Title";

    public string BodyKey => $"Template.{Id}.Body";
}

/// <summary>
/// Bibliothèque des modèles livrés avec Lettra (vague 5, lane F), insérables par « / » dans le champ ou par la palette
/// Ctrl+K. Les textes sont dans <c>Localization/Modules/Saisie[.en].resx</c> ; ce sont des formules d'usage, pas un
/// avis juridique : le contrôle de conformité reste celui du serveur.
/// </summary>
public static class TextTemplates
{
    private static readonly TextFieldKind[] Notes = [TextFieldKind.QuoteNotes, TextFieldKind.InvoiceNotes];

    public static IReadOnlyList<TextTemplate> All { get; } =
    [
        new("subject-bookkeeping", [TextFieldKind.Subject]),
        new("subject-annual", [TextFieldKind.Subject]),
        new("subject-payroll", [TextFieldKind.Subject]),
        new("subject-advice", [TextFieldKind.Subject]),
        new("terms-receipt", [TextFieldKind.PaymentTerms]),
        new("terms-30", [TextFieldKind.PaymentTerms]),
        new("terms-45-eom", [TextFieldKind.PaymentTerms]),
        new("terms-transfer", [TextFieldKind.PaymentTerms, TextFieldKind.InvoiceNotes]),
        new("terms-no-discount", [TextFieldKind.PaymentTerms, TextFieldKind.InvoiceNotes]),
        new("late-penalties", [TextFieldKind.PaymentTerms, TextFieldKind.InvoiceNotes]),
        new("quote-validity", [TextFieldKind.QuoteNotes]),
        new("quote-acceptance", [TextFieldKind.QuoteNotes]),
        new("quote-down-payment", [TextFieldKind.QuoteNotes]),
        new("vat-franchise", Notes),
        new("thanks", Notes),
    ];

    public static IEnumerable<TextTemplate> For(TextFieldKind field) => All.Where(t => t.Fields.Contains(field));
}
