using System.Globalization;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Sync;

/// <summary>Textes de la synchronisation partagés par la pastille, la demande et les écrans.</summary>
public static class SyncText
{
    /// <summary>« 2 factures validées, 1 création, 5 modifications (12 Ko) », ou « Rien à envoyer. ».</summary>
    public static string Summary(IStringLocalizer localizer, SyncPendingSummary summary)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(summary);
        if (summary.Total == 0)
        {
            return localizer["Sync.Summary.Nothing"];
        }

        IEnumerable<string> parts = new (int Count, string Key)[]
            {
                (summary.Validations, "Sync.Summary.Validations"),
                (summary.Creations, "Sync.Summary.Creations"),
                (summary.Changes, "Sync.Summary.Changes"),
            }
            .Where(p => p.Count > 0)
            .Select(p => string.Format(CultureInfo.CurrentCulture, localizer[p.Key], p.Count));
        return string.Format(CultureInfo.CurrentCulture, localizer["Sync.Summary.Text"], string.Join(", ", parts), Size(localizer, summary.EstimatedBytes));
    }

    public static string Size(IStringLocalizer localizer, long bytes)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return bytes switch
        {
            < 1024 => string.Format(CultureInfo.CurrentCulture, localizer["Sync.Size.Bytes"], bytes),
            < 1024 * 1024 => string.Format(CultureInfo.CurrentCulture, localizer["Sync.Size.Kilo"], Math.Ceiling(bytes / 1024d)),
            _ => string.Format(CultureInfo.CurrentCulture, localizer["Sync.Size.Mega"], Math.Round(bytes / (1024d * 1024d), 1)),
        };
    }

    /// <summary>Libellé d'un champ disputé, repris des écrans de saisie ; à défaut, le nom du champ.</summary>
    public static string FieldName(IStringLocalizer localizer, SyncItemKind item, string field)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        string[] prefixes = item switch
        {
            SyncItemKind.Customer => ["Customers.Field."],
            SyncItemKind.Supplier => ["Suppliers.Field.", "Customers.Field."],
            SyncItemKind.CatalogItem => ["Catalog.Column."],
            _ => [],
        };
        foreach (string prefix in prefixes)
        {
            LocalizedString text = localizer[prefix + field];
            if (!text.ResourceNotFound)
            {
                return text.Value;
            }
        }

        return field;
    }
}
