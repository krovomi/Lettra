using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Features.Kpi;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Pilotage;
using Lettra.Contracts.Tax;
using Lettra.Domain.Billing;
using Lettra.Domain.Prevention;
using Lettra.Domain.Vigilance;

namespace Lettra.Exports.Application.Insights.Prevention;

/// <summary>Une facture fournisseur reçue, ni refusée ni payée : ce qu'elle fera sortir de la trésorerie, et quand.</summary>
public sealed record PreventionPayable(DateOnly DueDate, decimal AmountInclTax);

/// <summary>Une échéance de TVA à venir, d'une période close (son montant est connu par les ventes validées).</summary>
public sealed record PreventionVatPayment(FiscalObligationDto Obligation, DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly DueDate, decimal Amount);

/// <summary>Un client actif du dossier et ce que sa dernière vérification auprès des registres a trouvé.</summary>
public sealed record PreventionCustomerCheck(Guid CustomerId, string Name, bool Checked, LegalNoticeKind? Notice, DateOnly? NoticeOn);

/// <summary>Tout ce que le radar lit dans le dossier, déjà chargé : le calcul ne touche à aucune base.</summary>
public sealed record PreventionInput(
    PreventionSettings Settings,
    IReadOnlyList<PaymentHistoryInvoice> Invoices,
    BillingKpiFacts Billing,
    IReadOnlyList<PreventionPayable> Payables,
    IReadOnlyList<PreventionVatPayment> VatPayments,
    PreventionUnavailableDto VatUnavailable,
    PeerComparisonResponse Peers,
    IReadOnlyList<PreventionCustomerCheck> Customers);

/// <summary>
/// Les règles du radar de prévention, en C# déterministe (aucune IA, aucun modèle appris) : chaque signal se calcule à
/// partir de ce que Lettra a déjà, dit sa raison par des chiffres, et s'allume selon un seuil que le cabinet règle.
/// <list type="bullet">
/// <item><b>Trésorerie</b> : solde connu (indiqué par le cabinet, de moins de 45 jours), plus les encaissements attendus
/// de la météo des paiements, moins les factures fournisseurs reçues à payer et la TVA des périodes closes, semaine par
/// semaine ; allumé si le solde passe sous zéro dans l'horizon, grave dans sa première moitié.</item>
/// <item><b>Délai de paiement</b> : délai moyen (encours sur ventes des 90 derniers jours) à la fin des quatre derniers
/// mois ; allumé s'il monte trois fois de suite d'au moins le seuil au total, grave au triple.</item>
/// <item><b>Pairs</b> : évolution du chiffre d'affaires (trois derniers mois sur les trois d'avant) comparée à la
/// médiane d'au moins cinq dossiers du même secteur ; allumé si elle est négative et en retard d'au moins le seuil, grave
/// au double.</item>
/// <item><b>TVA</b> : la prochaine échéance (45 jours) comparée aux encaissements attendus d'ici là ; allumé si la TVA
/// dépasse, grave à moins de 15 jours.</item>
/// <item><b>Premier client</b> : sa part du chiffre d'affaires HT des douze derniers mois ; allumé au seuil.</item>
/// <item><b>Procédure collective</b> : clients dont la dernière vérification au BODACC trouve une procédure ; grave
/// s'ils doivent encore de l'argent (la créance se déclare dans les deux mois de la publication).</item>
/// <item><b>Facturé et encaissé</b> : sur les trois derniers mois, chaque mois encaisse moins qu'il ne facture et la
/// part non encaissée atteint le seuil ; grave au double.</item>
/// </list>
/// Le niveau du dossier : calme sans signal allumé ; à appeler avec un signal grave ou au moins
/// <see cref="PreventionSettings.CallSignalCount"/> signaux ; à surveiller sinon.
/// </summary>
public static class PreventionRules
{
    /// <summary>Fenêtre du délai moyen de paiement, comme l'indicateur des tableaux de bord.</summary>
    public const int DsoWindowDays = BillingKpiCalculator.DsoWindowDays;

    /// <summary>Mois observés par les signaux de tendance (« trois mois de suite »).</summary>
    public const int TrendMonths = 3;

    /// <summary>La prochaine échéance de TVA est cherchée dans ce nombre de jours.</summary>
    public const int VatLookAheadDays = 45;

    /// <summary>Une échéance de TVA plus proche rend le signal grave.</summary>
    public const int VatUrgentDays = 15;

    /// <summary>Délai de déclaration de créance après la publication au BODACC (art. R622-24 du code de commerce).</summary>
    public const int ClaimDeclarationMonths = 2;

    /// <summary>Une facture fournisseur sans échéance est due trente jours après son émission (délai supplétif).</summary>
    public const int DefaultSupplierTermDays = 30;

    public static PreventionRadarResponse Compute(PreventionInput input, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(input);
        PreventionSettings settings = input.Settings;
        IReadOnlySet<PreventionSignal> disabled = settings.Disabled;
        PaymentWeatherResponse weather = PaymentWeatherForecast.Compute(input.Invoices, today);
        ExpectedCollectionResponse[] expected = [.. weather.Customers.SelectMany(c => c.Expected).Where(e => !e.Doubtful)];

        PreventionSignalResponse[] signals =
        [
            Guarded(PreventionSignal.Treasury, disabled, () => Treasury(input, expected, today)),
            Guarded(PreventionSignal.PaymentDelay, disabled, () => PaymentDelay(input, today)),
            Guarded(PreventionSignal.PeerGap, disabled, () => PeerGap(input)),
            Guarded(PreventionSignal.VatBeyondCollections, disabled, () => Vat(input, expected, today)),
            Guarded(PreventionSignal.TopCustomer, disabled, () => TopCustomer(input, today)),
            Guarded(PreventionSignal.CustomersInProceedings, disabled, () => Proceedings(input, today)),
            Guarded(PreventionSignal.BilledCollectedGap, disabled, () => BilledCollected(input, today)),
        ];
        int active = signals.Count(s => s.IsActive);
        PreventionLevelDto level = active == 0 ? PreventionLevelDto.Calm
            : signals.Any(s => s.State == PreventionSignalStateDto.Call) || active >= settings.CallSignalCount ? PreventionLevelDto.Call
            : PreventionLevelDto.Watch;
        return new PreventionRadarResponse(today, level, active, signals, Describe(settings));
    }

    /// <summary>
    /// L'indicateur de la vue cabinet : le nombre de signaux allumés et le niveau, l'échéance la plus proche. Aucun
    /// montant, aucun nom, aucune indication du signal : le détail se lit dans le dossier.
    /// </summary>
    public static DossierIndicator Indicator(PreventionRadarResponse radar)
    {
        ArgumentNullException.ThrowIfNull(radar);
        IndicatorLevel level = radar.Level switch
        {
            PreventionLevelDto.Call => IndicatorLevel.Critical,
            PreventionLevelDto.Watch => IndicatorLevel.Warning,
            _ => IndicatorLevel.Ok,
        };
        DateOnly? due = radar.Signals.Where(s => s.IsActive).Min(s => s.DueOn);
        return new DossierIndicator(DossierIndicatorKeys.Prevention, radar.ActiveSignals, level, due);
    }

    /// <summary>Le résumé de la carte du dossier : le niveau, les signaux allumés, l'échéance la plus proche.</summary>
    public static PreventionSummaryResponse Summarize(PreventionRadarResponse radar)
    {
        ArgumentNullException.ThrowIfNull(radar);
        PreventionSignalResponse[] active = [.. radar.Signals.Where(s => s.IsActive).OrderByDescending(s => s.State)];
        return new PreventionSummaryResponse(
            true, radar.Level, radar.ActiveSignals, [.. active.Select(s => s.Signal)], active.Min(s => s.DueOn));
    }

    public static PreventionSettingsResponse Describe(PreventionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new PreventionSettingsResponse(
            settings.TreasuryHorizonWeeks,
            settings.CashBalance,
            settings.CashBalanceOn,
            settings.PaymentDelayRiseDays,
            settings.PeerGapPoints,
            settings.TopCustomerSharePercent,
            settings.BilledCollectedGapPercent,
            settings.CallSignalCount,
            [.. settings.Disabled.Order().Select(s => (PreventionSignalDto)(int)s)],
            settings.Version,
            settings.UpdatedAt);
    }

    /// <summary>Le délai moyen de paiement à une date : l'encours rapporté aux ventes des 90 jours qui précèdent.</summary>
    public static decimal? DaysSalesOutstanding(IEnumerable<PaymentHistoryInvoice> invoices, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        decimal outstanding = 0m;
        decimal sales = 0m;
        DateOnly since = date.AddDays(-DsoWindowDays);
        foreach (PaymentHistoryInvoice invoice in invoices)
        {
            if (invoice.IssueDate is not DateOnly issued || issued > date)
            {
                continue;
            }

            outstanding += Math.Max(0m, invoice.AmountDue - invoice.Payments.Where(p => p.Date <= date).Sum(p => p.Amount));
            if (issued > since)
            {
                sales += invoice.AmountDue;
            }
        }

        return sales > 0m ? Math.Round(outstanding / sales * DsoWindowDays, 0, MidpointRounding.AwayFromZero) : null;
    }

    private static PreventionSignalResponse Guarded(PreventionSignal signal, IReadOnlySet<PreventionSignal> disabled, Func<PreventionSignalResponse> compute) =>
        disabled.Contains(signal) ? Empty((PreventionSignalDto)(int)signal, PreventionSignalStateDto.Disabled) : compute();

    private static PreventionSignalResponse Treasury(PreventionInput input, ExpectedCollectionResponse[] expected, DateOnly today)
    {
        PreventionSettings settings = input.Settings;
        if (settings.CashBalance is not decimal start || settings.CashBalanceOn is not DateOnly on)
        {
            return Unavailable(PreventionSignalDto.Treasury, PreventionUnavailableDto.NoCashBalance);
        }

        if (on < today.AddDays(-PreventionSettingsLimits.CashBalanceMaxAgeDays))
        {
            return Unavailable(PreventionSignalDto.Treasury, PreventionUnavailableDto.CashBalanceOutdated, [Date("cashBalanceOn", on)]);
        }

        // Semaine 0 = celle d'aujourd'hui (du lundi au dimanche) ; une facture fournisseur échue non payée sort tout de suite.
        DateOnly monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        int horizon = settings.TreasuryHorizonWeeks;
        List<PreventionWeekDto> weeks = [];
        decimal balance = start;
        for (int week = 0; week < horizon; week++)
        {
            DateOnly from = monday.AddDays(7 * week);
            DateOnly to = from.AddDays(6);
            decimal inflows = expected.Where(e => e.ExpectedDate >= from && e.ExpectedDate <= to).Sum(e => e.OutstandingInclTax);
            decimal outflows =
                input.Payables.Where(p => (week == 0 ? p.DueDate <= to : p.DueDate >= from && p.DueDate <= to)).Sum(p => p.AmountInclTax)
                + input.VatPayments.Where(v => v.DueDate >= from && v.DueDate <= to && v.DueDate >= today).Sum(v => v.Amount);
            balance += inflows - outflows;
            weeks.Add(new PreventionWeekDto(from, inflows, outflows, balance));
        }

        PreventionWeekDto lowest = weeks.MinBy(w => w.Balance)!;
        int negative = weeks.FindIndex(w => w.Balance < 0m);
        PreventionWeekDto shown = negative >= 0 ? weeks[negative] : lowest;
        PreventionFigureDto[] figures =
        [
            Date("week", shown.Start < today ? today : shown.Start),
            Amount("lowestBalance", lowest.Balance),
            Amount("cashBalance", start),
            Date("cashBalanceOn", on),
            Count("horizonWeeks", horizon),
        ];
        if (negative < 0)
        {
            return Clear(PreventionSignalDto.Treasury, figures) with { Weeks = weeks };
        }

        PreventionSignalStateDto state = negative < (horizon + 1) / 2 ? PreventionSignalStateDto.Call : PreventionSignalStateDto.Watch;
        return new PreventionSignalResponse(
            PreventionSignalDto.Treasury, state, PreventionUnavailableDto.None, -lowest.Balance, shown.Start < today ? today : shown.Start, figures, weeks, [], []);
    }

    private static PreventionSignalResponse PaymentDelay(PreventionInput input, DateOnly today)
    {
        DateOnly month = new(today.Year, today.Month, 1);
        DateOnly[] ends = [.. Enumerable.Range(0, TrendMonths + 1).Select(i => month.AddMonths(i - TrendMonths).AddDays(-1))];
        decimal?[] dso = [.. ends.Select(end => DaysSalesOutstanding(input.Invoices, end))];
        if (dso.Any(d => d is null))
        {
            return Unavailable(PreventionSignalDto.PaymentDelay, PreventionUnavailableDto.NotEnoughHistory);
        }

        decimal[] values = [.. dso.Select(d => d!.Value)];
        decimal rise = values[^1] - values[0];
        DateOnly since = ends[^1].AddDays(-DsoWindowDays);
        decimal recentSales = input.Invoices.Where(i => i.IssueDate is DateOnly d && d > since && d <= ends[^1]).Sum(i => i.AmountDue);
        decimal tiedUp = rise > 0m ? Math.Round(rise / DsoWindowDays * recentSales, 2, MidpointRounding.AwayFromZero) : 0m;
        PreventionMonthDto[] months = [.. ends.Select((end, i) => new PreventionMonthDto(new DateOnly(end.Year, end.Month, 1), values[i], null, null))];
        int threshold = input.Settings.PaymentDelayRiseDays;
        PreventionFigureDto[] figures =
        [
            Days("from", values[0]),
            Days("to", values[^1]),
            Days("rise", rise),
            Days("threshold", threshold),
            Amount("tiedUp", tiedUp),
        ];
        bool rising = values.Zip(values.Skip(1)).All(p => p.Second > p.First);
        if (!rising || rise < threshold)
        {
            return Clear(PreventionSignalDto.PaymentDelay, figures) with { Months = months };
        }

        PreventionSignalStateDto state = rise >= 3 * threshold ? PreventionSignalStateDto.Call : PreventionSignalStateDto.Watch;
        return new PreventionSignalResponse(PreventionSignalDto.PaymentDelay, state, PreventionUnavailableDto.None, tiedUp, null, figures, [], months, []);
    }

    private static PreventionSignalResponse PeerGap(PreventionInput input)
    {
        PeerComparisonResponse peers = input.Peers;
        PreventionUnavailableDto missing = peers.Status switch
        {
            PeerComparisonStatusDto.NoSector => PreventionUnavailableDto.NoSector,
            PeerComparisonStatusDto.NoCollection => PreventionUnavailableDto.NoCollection,
            PeerComparisonStatusDto.TooFewPeers => PreventionUnavailableDto.TooFewPeers,
            _ => PreventionUnavailableDto.None,
        };
        if (missing != PreventionUnavailableDto.None)
        {
            return Unavailable(PreventionSignalDto.PeerGap, missing);
        }

        if (peers.Metrics.FirstOrDefault(m => m.Metric == PeerMetricDto.RevenueTrend) is not { } trend)
        {
            return Unavailable(PreventionSignalDto.PeerGap, PreventionUnavailableDto.TooFewPeers);
        }

        if (trend.Value is not double own)
        {
            return Unavailable(PreventionSignalDto.PeerGap, PreventionUnavailableDto.NoTrend);
        }

        decimal mine = Math.Round((decimal)own * 100m, 1, MidpointRounding.AwayFromZero);
        decimal median = Math.Round((decimal)trend.Median * 100m, 1, MidpointRounding.AwayFromZero);
        decimal gap = median - mine;
        int threshold = input.Settings.PeerGapPoints;
        PreventionFigureDto[] figures =
        [
            Percent("trend", mine),
            Percent("median", median),
            Count("gapPoints", Math.Round(gap, 0, MidpointRounding.AwayFromZero)),
            Count("peers", trend.Peers),
            Count("threshold", threshold),
        ];
        if (mine >= 0m || gap < threshold)
        {
            return Clear(PreventionSignalDto.PeerGap, figures);
        }

        PreventionSignalStateDto state = gap >= 2 * threshold ? PreventionSignalStateDto.Call : PreventionSignalStateDto.Watch;
        return new PreventionSignalResponse(PreventionSignalDto.PeerGap, state, PreventionUnavailableDto.None, null, null, figures, [], [], []);
    }

    private static PreventionSignalResponse Vat(PreventionInput input, ExpectedCollectionResponse[] expected, DateOnly today)
    {
        if (input.VatUnavailable != PreventionUnavailableDto.None)
        {
            return Unavailable(PreventionSignalDto.VatBeyondCollections, input.VatUnavailable);
        }

        PreventionVatPayment? next = input.VatPayments
            .Where(v => v.DueDate >= today && v.DueDate <= today.AddDays(VatLookAheadDays))
            .OrderBy(v => v.DueDate)
            .FirstOrDefault();
        if (next is null)
        {
            return Unavailable(PreventionSignalDto.VatBeyondCollections, PreventionUnavailableDto.NoVatDeadline);
        }

        decimal collections = expected.Where(e => e.ExpectedDate <= next.DueDate).Sum(e => e.OutstandingInclTax);
        PreventionFigureDto[] figures =
        [
            Text("obligation", next.Obligation.ToString()),
            Date("dueDate", next.DueDate),
            Amount("vat", next.Amount),
            Amount("collections", collections),
            Date("periodStart", next.PeriodStart),
            Date("periodEnd", next.PeriodEnd),
        ];
        if (next.Amount <= 0m || next.Amount <= collections)
        {
            return Clear(PreventionSignalDto.VatBeyondCollections, figures);
        }

        PreventionSignalStateDto state = next.DueDate <= today.AddDays(VatUrgentDays) ? PreventionSignalStateDto.Call : PreventionSignalStateDto.Watch;
        return new PreventionSignalResponse(
            PreventionSignalDto.VatBeyondCollections, state, PreventionUnavailableDto.None, next.Amount - collections, next.DueDate, figures, [], [], []);
    }

    private static PreventionSignalResponse TopCustomer(PreventionInput input, DateOnly today)
    {
        DateOnly windowStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-(BillingKpiCalculator.Months - 1));
        (Guid Customer, decimal Revenue)[] perCustomer =
        [
            .. input.Billing.Invoices
                .Where(i => i.IssueDate >= windowStart && i.IssueDate <= today)
                .GroupBy(i => i.CustomerId)
                .Select(g => (g.Key, g.Sum(i => (i.Kind == InvoiceKind.CreditNote ? -1m : 1m) * i.TotalExclTax))),
        ];
        decimal total = perCustomer.Sum(c => c.Revenue);
        if (total <= 0m)
        {
            return Unavailable(PreventionSignalDto.TopCustomer, PreventionUnavailableDto.NoRevenue);
        }

        (Guid id, decimal revenue) = perCustomer.MaxBy(c => c.Revenue);
        decimal share = Math.Round(Math.Clamp(revenue / total, 0m, 1m) * 100m, 1, MidpointRounding.AwayFromZero);
        string name = NameOf(input, id);
        int threshold = input.Settings.TopCustomerSharePercent;
        PreventionFigureDto[] figures =
        [
            Text("customer", name),
            Percent("share", share),
            Percent("threshold", threshold),
            Amount("revenue", revenue),
            Amount("total", total),
        ];
        PreventionPartyDto[] parties = [new(id, name, revenue, null)];
        return share < threshold
            ? Clear(PreventionSignalDto.TopCustomer, figures) with { Parties = parties }
            : new PreventionSignalResponse(PreventionSignalDto.TopCustomer, PreventionSignalStateDto.Watch, PreventionUnavailableDto.None, revenue, null, figures, [], [], parties);
    }

    private static PreventionSignalResponse Proceedings(PreventionInput input, DateOnly today)
    {
        int checkedCount = input.Customers.Count(c => c.Checked);
        if (checkedCount == 0)
        {
            return Unavailable(PreventionSignalDto.CustomersInProceedings, PreventionUnavailableDto.NoCustomerChecked, [Count("customers", input.Customers.Count)]);
        }

        Dictionary<Guid, decimal> owed = input.Invoices
            .Where(i => i.Outstanding > 0m)
            .GroupBy(i => i.CustomerId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Outstanding));
        PreventionPartyDto[] parties =
        [
            .. input.Customers
                .Where(c => c.Notice == LegalNoticeKind.CollectiveProceeding)
                .Select(c => new PreventionPartyDto(c.CustomerId, c.Name, owed.GetValueOrDefault(c.CustomerId), c.NoticeOn))
                .OrderByDescending(p => p.Amount)
                .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
        ];
        decimal outstanding = parties.Sum(p => p.Amount);
        PreventionFigureDto[] figures =
        [
            Count("count", parties.Length),
            Amount("outstanding", outstanding),
            Count("checked", checkedCount),
            Count("customers", input.Customers.Count),
        ];
        if (parties.Length == 0)
        {
            return Clear(PreventionSignalDto.CustomersInProceedings, figures);
        }

        DateOnly? claimBy = parties
            .Where(p => p.Amount > 0m && p.Since is not null && p.Since.Value.AddMonths(ClaimDeclarationMonths) >= today)
            .Min(p => p.Since?.AddMonths(ClaimDeclarationMonths));
        PreventionSignalStateDto state = outstanding > 0m ? PreventionSignalStateDto.Call : PreventionSignalStateDto.Watch;
        return new PreventionSignalResponse(
            PreventionSignalDto.CustomersInProceedings, state, PreventionUnavailableDto.None, outstanding > 0m ? outstanding : null, claimBy, figures, [], [], parties);
    }

    private static PreventionSignalResponse BilledCollected(PreventionInput input, DateOnly today)
    {
        DateOnly current = new(today.Year, today.Month, 1);
        PreventionMonthDto[] months =
        [
            .. Enumerable.Range(0, TrendMonths).Select(i =>
            {
                DateOnly from = current.AddMonths(i - TrendMonths);
                DateOnly to = from.AddMonths(1);
                decimal billed = input.Invoices.Where(inv => inv.IssueDate is DateOnly d && d >= from && d < to).Sum(inv => inv.AmountDue);
                decimal collected = input.Invoices.SelectMany(inv => inv.Payments).Where(p => p.Date >= from && p.Date < to).Sum(p => p.Amount);
                return new PreventionMonthDto(from, null, billed, collected);
            }),
        ];
        decimal billedTotal = months.Sum(m => m.Billed!.Value);
        if (billedTotal <= 0m)
        {
            return Unavailable(PreventionSignalDto.BilledCollectedGap, PreventionUnavailableDto.NotEnoughHistory);
        }

        decimal collectedTotal = months.Sum(m => m.Collected!.Value);
        decimal gap = billedTotal - collectedTotal;
        decimal percent = Math.Round(gap / billedTotal * 100m, 1, MidpointRounding.AwayFromZero);
        int threshold = input.Settings.BilledCollectedGapPercent;
        PreventionFigureDto[] figures =
        [
            Amount("billed", billedTotal),
            Amount("collected", collectedTotal),
            Amount("gap", gap),
            Percent("gapPercent", percent),
            Count("months", TrendMonths),
            Percent("threshold", threshold),
        ];
        bool growing = months.All(m => m.Billed > m.Collected);
        if (!growing || percent < threshold)
        {
            return Clear(PreventionSignalDto.BilledCollectedGap, figures) with { Months = months };
        }

        PreventionSignalStateDto state = percent >= 2 * threshold ? PreventionSignalStateDto.Call : PreventionSignalStateDto.Watch;
        return new PreventionSignalResponse(PreventionSignalDto.BilledCollectedGap, state, PreventionUnavailableDto.None, gap, null, figures, [], months, []);
    }

    private static string NameOf(PreventionInput input, Guid customer) =>
        input.Customers.FirstOrDefault(c => c.CustomerId == customer)?.Name
        ?? input.Invoices.FirstOrDefault(i => i.CustomerId == customer)?.CustomerName
        ?? string.Empty;

    private static PreventionSignalResponse Empty(PreventionSignalDto signal, PreventionSignalStateDto state) =>
        new(signal, state, PreventionUnavailableDto.None, null, null, [], [], [], []);

    private static PreventionSignalResponse Clear(PreventionSignalDto signal, IReadOnlyList<PreventionFigureDto> figures) =>
        Empty(signal, PreventionSignalStateDto.Clear) with { Figures = figures };

    private static PreventionSignalResponse Unavailable(PreventionSignalDto signal, PreventionUnavailableDto reason, IReadOnlyList<PreventionFigureDto>? figures = null) =>
        Empty(signal, PreventionSignalStateDto.NotEvaluated) with { Unavailable = reason, Figures = figures ?? [] };

    private static PreventionFigureDto Amount(string name, decimal value) => new(name, PreventionFigureKindDto.Amount, Math.Round(value, 2, MidpointRounding.AwayFromZero));

    private static PreventionFigureDto Days(string name, decimal value) => new(name, PreventionFigureKindDto.Days, value);

    private static PreventionFigureDto Percent(string name, decimal value) => new(name, PreventionFigureKindDto.Percent, value);

    private static PreventionFigureDto Count(string name, decimal value) => new(name, PreventionFigureKindDto.Count, value);

    private static PreventionFigureDto Date(string name, DateOnly value) => new(name, PreventionFigureKindDto.Date, Date: value);

    private static PreventionFigureDto Text(string name, string value) => new(name, PreventionFigureKindDto.Text, Text: value);
}
