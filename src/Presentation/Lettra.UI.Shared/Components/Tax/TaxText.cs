using System.Globalization;
using Lettra.Contracts.Common;
using Lettra.Contracts.Tax;
using Lettra.UI.Shared.Components.Explorer;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Components.Tax;

/// <summary>Mise en forme commune des écrans de déclarations : montants, périodes, échéances.</summary>
public static class TaxText
{
    /// <summary>Codes de refus qui ne sont pas des erreurs : l'écran explique quoi compléter au lieu d'afficher un toast.</summary>
    public static bool IsSetupNeeded(IReadOnlyList<ErrorDetail> errors) =>
        errors.Any(e => e.Code is ErrorCodes.Tax.ProfileMissing or ErrorCodes.Tax.RegimeMissing or ErrorCodes.Tax.VatFranchise);

    /// <summary>Montant arrondi à l'euro, comme le formulaire le demande (1 240 €).</summary>
    public static string Euros(decimal value) =>
        Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.CurrentCulture) + " €";

    /// <summary>Montant au centime (détail des pièces).</summary>
    public static string Exact(decimal value) => ExplorerText.Money(value);

    public static string Day(DateOnly date) => date.ToString("d MMMM yyyy", CultureInfo.CurrentCulture);

    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Month(DateOnly date) => Capitalize(date.ToString("MMMM yyyy", CultureInfo.CurrentCulture));

    /// <summary>« Septembre 2026 », « 3e trimestre 2026 », « Exercice du 1 janvier au 31 décembre 2026 ».</summary>
    public static string Period(IStringLocalizer localizer, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        int months = ((to.Year - from.Year) * 12) + to.Month - from.Month + 1;
        return months switch
        {
            1 => Month(from),
            3 when from.Day == 1 && (from.Month - 1) % 3 == 0 => string.Format(CultureInfo.CurrentCulture, localizer["Tax.Period.Quarter"], ((from.Month - 1) / 3) + 1, from.Year),
            _ => string.Format(CultureInfo.CurrentCulture, localizer["Tax.Period.Range"], Day(from), Day(to)),
        };
    }

    /// <summary>Lien vers une pièce : la facture, l'acompte et l'avoir s'ouvrent dans le même écran.</summary>
    public static string PieceHref(TaxSourceResponse source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return $"invoices/{source.InvoiceId}";
    }

    public static string StateClass(FiscalDeadlineStateDto state) => state switch
    {
        FiscalDeadlineStateDto.Overdue => "is-danger",
        FiscalDeadlineStateDto.DueSoon => "is-warning",
        FiscalDeadlineStateDto.Filed => "is-success",
        _ => "is-neutral",
    };

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];
}
