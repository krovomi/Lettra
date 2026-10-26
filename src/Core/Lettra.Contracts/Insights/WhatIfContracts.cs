using Lettra.Contracts.Tax;

namespace Lettra.Contracts.Insights;

/// <summary>Le régime de TVA essayé par le simulateur (valeurs stables, ajout seulement).</summary>
public enum WhatIfVatDto
{
    /// <summary>Le régime des réglages fiscaux du dossier.</summary>
    Current = 0,

    /// <summary>CA3 mensuelle.</summary>
    Monthly = 1,

    /// <summary>CA3 trimestrielle.</summary>
    Quarterly = 2,

    /// <summary>Franchise en base : pas de TVA facturée ni reversée.</summary>
    Franchise = 3,
}

/// <summary>Ce qu'un effet du scénario change dans la trésorerie (valeurs stables, ajout seulement).</summary>
public enum WhatIfEffectKindDto
{
    /// <summary>Un encaissement attendu qui glisse à cause du délai accordé.</summary>
    CollectionMoved = 0,

    /// <summary>L'encaissement de la facture essayée.</summary>
    InvoiceCollected = 1,

    /// <summary>La TVA de la facture essayée, reversée à l'échéance de sa déclaration.</summary>
    VatPaid = 2,

    /// <summary>Un mois de salaire chargé de l'embauche essayée.</summary>
    SalaryPaid = 3,
}

/// <summary>
/// Un scénario du simulateur « et si ? ». Chaque levier est facultatif : sans aucun, la réponse est la trésorerie
/// attendue telle quelle. Rien n'est écrit dans le dossier.
/// </summary>
/// <param name="ExtraDelayDays">Jours de délai accordés en plus aux clients (0 à <see cref="MaxExtraDelayDays"/>).</param>
/// <param name="Invoice">Une facture à émettre, pour voir son effet sur le chiffre d'affaires, la TVA et la trésorerie.</param>
/// <param name="Vat">Le régime de TVA essayé.</param>
/// <param name="Hire">Une embauche à essayer.</param>
public sealed record WhatIfRequest(
    int ExtraDelayDays = 0,
    WhatIfInvoiceRequest? Invoice = null,
    WhatIfVatDto Vat = WhatIfVatDto.Current,
    WhatIfHireRequest? Hire = null)
{
    public const int MaxExtraDelayDays = 120;

    /// <summary>Semaines simulées, du lundi de la semaine de demain : un trimestre.</summary>
    public const int Weeks = 13;
}

/// <summary>Une facture essayée : montant hors taxes, taux de TVA, date d'émission et délai de paiement.</summary>
/// <param name="AmountExclTax">Montant hors taxes.</param>
/// <param name="VatRatePercent">Taux de TVA : 0, 2,1, 5,5, 10 ou 20 %.</param>
/// <param name="IssueDate">Date d'émission, aujourd'hui au plus tôt.</param>
/// <param name="PaymentTermDays">Délai de paiement accordé, en jours (au plus 60, la limite légale).</param>
public sealed record WhatIfInvoiceRequest(decimal AmountExclTax, decimal VatRatePercent, DateOnly IssueDate, int PaymentTermDays)
{
    public const decimal MaxAmount = 10_000_000m;
    public const int MaxPaymentTermDays = 60;
}

/// <summary>Une embauche essayée : salaire brut mensuel, date d'arrivée et taux de charges patronales.</summary>
public sealed record WhatIfHireRequest(decimal MonthlyGrossSalary, DateOnly StartDate, decimal EmployerChargesPercent)
{
    public const decimal MaxSalary = 100_000m;

    /// <summary>Taux proposé par défaut, à ajuster avec le cabinet.</summary>
    public const decimal DefaultEmployerChargesPercent = 45m;
}

/// <summary>Une semaine simulée : ce qui entre sans le scénario, ce qui entre et sort avec, et l'écart cumulé.</summary>
public sealed record WhatIfWeekResponse(DateOnly WeekStart, decimal BaselineIn, decimal ScenarioIn, decimal ScenarioOut, decimal CumulativeGap);

/// <summary>Un mouvement de trésorerie que le scénario ajoute, retire ou déplace (positif : entrée, négatif : sortie).</summary>
/// <param name="Kind">La nature de l'effet.</param>
/// <param name="Date">Le jour où il touche la trésorerie.</param>
/// <param name="Amount">Son montant, positif pour une entrée, négatif pour une sortie.</param>
/// <param name="Label">Le numéro de la facture concernée, s'il y en a une.</param>
/// <param name="From">Pour un encaissement déplacé : la date attendue sans le scénario.</param>
public sealed record WhatIfEffectResponse(WhatIfEffectKindDto Kind, DateOnly Date, decimal Amount, string? Label = null, DateOnly? From = null);

/// <summary>L'effet de la facture essayée.</summary>
/// <param name="AmountExclTax">Montant hors taxes.</param>
/// <param name="Vat">TVA facturée (zéro en franchise).</param>
/// <param name="TotalInclTax">Total TTC encaissé.</param>
/// <param name="RevenueMonth">Premier jour du mois où son chiffre d'affaires compte.</param>
/// <param name="FiscalYearEnd">Clôture de l'exercice où son chiffre d'affaires compte.</param>
/// <param name="VatPeriodStart">Premier jour de la période de TVA qui la déclare (rien en franchise).</param>
/// <param name="VatPeriodEnd">Dernier jour de cette période.</param>
/// <param name="VatDueDate">Échéance de cette déclaration.</param>
/// <param name="ExpectedCollection">Échéance de la facture, décalée du délai accordé.</param>
public sealed record WhatIfInvoiceResponse(
    decimal AmountExclTax,
    decimal Vat,
    decimal TotalInclTax,
    DateOnly RevenueMonth,
    DateOnly FiscalYearEnd,
    DateOnly? VatPeriodStart,
    DateOnly? VatPeriodEnd,
    DateOnly? VatDueDate,
    DateOnly ExpectedCollection);

/// <summary>L'effet de l'embauche essayée : coût chargé d'un mois plein, sur les semaines simulées et sur un an.</summary>
public sealed record WhatIfHireResponse(decimal MonthlyCost, decimal CostInHorizon, decimal FirstYearCost);

/// <summary>
/// La réponse du simulateur : la trésorerie attendue sans et avec le scénario, semaine par semaine, et le détail de
/// chaque effet. Tout est calculé en C# à partir des données du dossier et des hypothèses affichées.
/// </summary>
/// <param name="Today">Jour légal (Paris) du calcul.</param>
/// <param name="CurrentPeriodicity">Périodicité de TVA des réglages du dossier.</param>
/// <param name="AppliedVat">Régime retenu pour le calcul.</param>
/// <param name="BaselineIn">Encaissements attendus sur les semaines simulées, sans le scénario.</param>
/// <param name="ScenarioIn">Encaissements attendus avec le scénario.</param>
/// <param name="ScenarioOut">Sorties ajoutées par le scénario (TVA, salaires), en positif.</param>
/// <param name="Gap">Écart de trésorerie à la fin des semaines simulées : entrées en plus moins sorties en plus.</param>
/// <param name="Weeks">Les semaines simulées, du lundi de la semaine de demain.</param>
/// <param name="Effects">Les mouvements ajoutés ou déplacés par le scénario, par date.</param>
/// <param name="Invoice">L'effet de la facture essayée, s'il y en a une.</param>
/// <param name="Hire">L'effet de l'embauche essayée, s'il y en a une.</param>
public sealed record WhatIfResponse(
    DateOnly Today,
    VatPeriodicityDto CurrentPeriodicity,
    WhatIfVatDto AppliedVat,
    decimal BaselineIn,
    decimal ScenarioIn,
    decimal ScenarioOut,
    decimal Gap,
    IReadOnlyList<WhatIfWeekResponse> Weeks,
    IReadOnlyList<WhatIfEffectResponse> Effects,
    WhatIfInvoiceResponse? Invoice,
    WhatIfHireResponse? Hire);
