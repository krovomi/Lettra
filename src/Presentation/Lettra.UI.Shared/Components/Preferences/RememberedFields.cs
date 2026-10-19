using Lettra.Contracts.Preferences;

namespace Lettra.UI.Shared.Components.Preferences;

/// <summary>Champs de saisie dont la valeur peut être retenue pour les prochaines pièces d'un dossier.</summary>
public enum RememberedField
{
    PaymentTerms,
    InvoiceNotes,
    QuoteNotes,
}

public static class RememberedFields
{
    public static string? Read(EntryDefaultsDto defaults, RememberedField field)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        return field switch
        {
            RememberedField.PaymentTerms => defaults.PaymentTerms,
            RememberedField.InvoiceNotes => defaults.InvoiceNotes,
            _ => defaults.QuoteNotes,
        };
    }

    public static EntryDefaultsDto Write(EntryDefaultsDto defaults, RememberedField field, string? value)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        return field switch
        {
            RememberedField.PaymentTerms => defaults with { PaymentTerms = value },
            RememberedField.InvoiceNotes => defaults with { InvoiceNotes = value },
            _ => defaults with { QuoteNotes = value },
        };
    }
}
