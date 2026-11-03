using System.Globalization;
using Lettra.Contracts.Tax;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Components.Tax;

/// <summary>Mise en forme des seuils qui approchent, partagée par l'onglet « Seuils » et les cartes de l'accueil et de « En bref ».</summary>
public static class ThresholdText
{
    /// <summary>L'écran où agir : la fiche entreprise pour la franchise, le régime et la catégorie, sinon l'écran du sujet.</summary>
    public static string Href(ThresholdFamilyDto family) => family switch
    {
        ThresholdFamilyDto.VatQuarterlyReturn => "tax/settings",
        ThresholdFamilyDto.SubcontractorVigilance => "attestations",
        ThresholdFamilyDto.FeeReturn => "tax/fees",
        _ => "settings/company",
    };

    /// <summary>La clé d'une ligne : le code du seuil, suivi du tiers pour un fournisseur ou un bénéficiaire.</summary>
    public static string Key(ThresholdWatchResponse row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.PartyId is { } party ? $"{row.Code}-{party}" : row.Code;
    }

    /// <summary>L'identifiant HTML d'une ligne (« threshold-tva-franchise-services-majore »).</summary>
    public static string ElementId(ThresholdWatchResponse row) => "threshold-" + Key(row).Replace('.', '-');

    public static string Icon(ThresholdFamilyDto family) => family switch
    {
        ThresholdFamilyDto.CompanySize => "building-2",
        ThresholdFamilyDto.SubcontractorVigilance => "file-check",
        ThresholdFamilyDto.FeeReturn => "hand-coins",
        _ => "receipt",
    };

    public static string StateClass(ThresholdStatusDto status) => status switch
    {
        ThresholdStatusDto.Crossed => "is-danger",
        ThresholdStatusDto.Projected => "is-warning",
        ThresholdStatusDto.Near => "is-info",
        _ => "is-neutral",
    };

    /// <summary>Le libellé du seuil ; celui d'un fournisseur ou d'un bénéficiaire porte son nom.</summary>
    public static string Label(IStringLocalizer localizer, ThresholdWatchResponse row)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(row);
        return string.Format(CultureInfo.CurrentCulture, localizer[$"Thresholds.Code.{row.Code}"], row.PartyName);
    }

    /// <summary>« Franchi le 12 septembre 2026 », « Prévu vers le 14 mars 2027 », sinon l'état.</summary>
    public static string Status(IStringLocalizer localizer, ThresholdWatchResponse row)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(row);
        return row switch
        {
            { Status: ThresholdStatusDto.Crossed, CrossedOn: { } on } => string.Format(CultureInfo.CurrentCulture, localizer["Thresholds.Status.CrossedOn"], TaxText.Day(on)),
            { Status: ThresholdStatusDto.Projected, ExpectedOn: { } on } => string.Format(CultureInfo.CurrentCulture, localizer["Thresholds.Status.ExpectedOn"], TaxText.Day(on)),
            _ => localizer[$"Thresholds.Status.{row.Status}"],
        };
    }

    /// <summary>Ce qui change au franchissement, sans date ni montant (le montant du seuil vient du catalogue).</summary>
    public static string Change(IStringLocalizer localizer, ThresholdWatchResponse row)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(row);
        return localizer[$"Thresholds.Change.{row.Code}"];
    }

    /// <summary>« Les règles changent le 1er janvier 2027 » quand le franchissement est constaté ou prévu, sinon <c>null</c>.</summary>
    public static string? Effective(IStringLocalizer localizer, ThresholdWatchResponse row)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(row);
        return row.EffectiveOn is { } on ? string.Format(CultureInfo.CurrentCulture, localizer["Thresholds.Effective"], TaxText.Day(on)) : null;
    }

    /// <summary>« 12 340 € sur 37 500 € HT ».</summary>
    public static string Amounts(IStringLocalizer localizer, ThresholdWatchResponse row)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(row);
        return string.Format(CultureInfo.CurrentCulture, localizer["Thresholds.Amounts"], TaxText.Euros(row.ToDate), TaxText.Euros(row.Threshold), localizer[$"Thresholds.Basis.{row.Basis}"]);
    }

    /// <summary>La part du seuil atteinte, en pourcentage borné à 100 pour la barre (le texte garde la vraie valeur).</summary>
    public static int Percent(decimal amount, decimal threshold) =>
        threshold <= 0m ? 0 : (int)Math.Clamp(Math.Round(100m * amount / threshold, MidpointRounding.AwayFromZero), 0m, 100m);

    /// <summary>La période du seuil : l'année civile (« Année 2026 ») ou l'exercice.</summary>
    public static string Period(IStringLocalizer localizer, ThresholdWatchResponse row)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(row);
        return row.PeriodStart is { Month: 1, Day: 1 } && row.PeriodEnd == new DateOnly(row.PeriodStart.Year, 12, 31)
            ? string.Format(CultureInfo.CurrentCulture, localizer["Thresholds.Period.Year"], row.PeriodStart.Year)
            : TaxText.Period(localizer, row.PeriodStart, row.PeriodEnd);
    }
}
