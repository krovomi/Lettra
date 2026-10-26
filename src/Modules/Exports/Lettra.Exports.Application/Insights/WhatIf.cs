using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Tax;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Tax;

namespace Lettra.Exports.Application.Insights;

/// <summary>Le simulateur « et si ? » : un scénario essayé sur la trésorerie attendue du dossier, sans rien écrire.</summary>
public sealed record SimulateWhatIfQuery(DossierId Dossier, WhatIfRequest Scenario) : IQuery<Result<WhatIfResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>
/// Le calcul du simulateur, pur et déterministe, avec les règles du dossier et des hypothèses dites à l'écran :
/// <list type="bullet">
/// <item>la base est la prévision de la météo des paiements (chaque facture due, attendue à l'échéance décalée du retard
/// habituel du client ; les douteuses sont hors prévision), sur <see cref="WhatIfRequest.Weeks"/> semaines ;</item>
/// <item>un délai accordé décale chaque encaissement attendu d'autant de jours ;</item>
/// <item>la facture essayée compte dans le chiffre d'affaires de son mois d'émission ; sa TVA est déclarée sur la
/// période de cette date (TVA sur les débits) et reversée à l'échéance du calendrier fiscal ; elle est encaissée à son
/// échéance, décalée du délai accordé ; en franchise en base, elle n'a pas de TVA ;</item>
/// <item>l'embauche coûte le salaire brut plus les charges patronales, payé le dernier jour de chaque mois, au prorata
/// des jours le premier mois.</item>
/// </list>
/// </summary>
public static class WhatIfSimulator
{
    public static readonly IReadOnlySet<decimal> VatRates = new HashSet<decimal> { 0m, 2.1m, 5.5m, 10m, 20m };

    /// <summary>Les champs du scénario qui sortent des bornes ; vide quand il est valable.</summary>
    public static IReadOnlyList<string> Invalid(WhatIfRequest scenario, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        List<string> invalid = [];
        if (scenario.ExtraDelayDays is < 0 or > WhatIfRequest.MaxExtraDelayDays)
        {
            invalid.Add(nameof(WhatIfRequest.ExtraDelayDays));
        }

        if (!Enum.IsDefined(scenario.Vat))
        {
            invalid.Add(nameof(WhatIfRequest.Vat));
        }

        if (scenario.Invoice is { } invoice)
        {
            if (invoice.AmountExclTax is <= 0m or > WhatIfInvoiceRequest.MaxAmount)
            {
                invalid.Add("Invoice.AmountExclTax");
            }

            if (!VatRates.Contains(invoice.VatRatePercent))
            {
                invalid.Add("Invoice.VatRatePercent");
            }

            if (invoice.IssueDate < today || invoice.IssueDate > today.AddDays(366))
            {
                invalid.Add("Invoice.IssueDate");
            }

            if (invoice.PaymentTermDays is < 0 or > WhatIfInvoiceRequest.MaxPaymentTermDays)
            {
                invalid.Add("Invoice.PaymentTermDays");
            }
        }

        if (scenario.Hire is { } hire)
        {
            if (hire.MonthlyGrossSalary is <= 0m or > WhatIfHireRequest.MaxSalary)
            {
                invalid.Add("Hire.MonthlyGrossSalary");
            }

            if (hire.EmployerChargesPercent is < 0m or > 100m)
            {
                invalid.Add("Hire.EmployerChargesPercent");
            }

            if (hire.StartDate < today.AddDays(-366) || hire.StartDate > today.AddDays(366))
            {
                invalid.Add("Hire.StartDate");
            }
        }

        return invalid;
    }

    public static WhatIfResponse Simulate(IEnumerable<ExpectedCollectionResponse> expected, FiscalSettings settings, WhatIfRequest scenario, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(scenario);
        DateOnly tomorrow = today.AddDays(1);
        DateOnly firstWeek = tomorrow.AddDays(-(((int)tomorrow.DayOfWeek + 6) % 7));
        DateOnly horizon = firstWeek.AddDays(7 * WhatIfRequest.Weeks);
        bool InHorizon(DateOnly date) => date >= firstWeek && date < horizon;

        ExpectedCollectionResponse[] due = [.. expected.Where(e => !e.Doubtful)];
        List<(DateOnly Date, decimal Amount)> baseline = [.. due.Select(e => (e.ExpectedDate, e.OutstandingInclTax))];
        List<(DateOnly Date, decimal Amount)> scenarioIn = [.. due.Select(e => (e.ExpectedDate.AddDays(scenario.ExtraDelayDays), e.OutstandingInclTax))];
        List<(DateOnly Date, decimal Amount)> scenarioOut = [];
        List<WhatIfEffectResponse> effects = [];
        if (scenario.ExtraDelayDays > 0)
        {
            effects.AddRange(due
                .Where(e => InHorizon(e.ExpectedDate))
                .Select(e => new WhatIfEffectResponse(WhatIfEffectKindDto.CollectionMoved, e.ExpectedDate.AddDays(scenario.ExtraDelayDays), e.OutstandingInclTax, e.Number, e.ExpectedDate)));
        }

        WhatIfVatDto vat = scenario.Vat == WhatIfVatDto.Current
            ? settings.VatPeriodicity == VatPeriodicityDto.Quarterly ? WhatIfVatDto.Quarterly : WhatIfVatDto.Monthly
            : scenario.Vat;
        WhatIfInvoiceResponse? invoice = scenario.Invoice is { } trial ? Invoice(trial, vat, settings, scenario.ExtraDelayDays, tomorrow) : null;
        if (invoice is not null)
        {
            scenarioIn.Add((invoice.ExpectedCollection, invoice.TotalInclTax));
            effects.Add(new WhatIfEffectResponse(WhatIfEffectKindDto.InvoiceCollected, invoice.ExpectedCollection, invoice.TotalInclTax));
            if (invoice is { Vat: > 0m, VatDueDate: { } vatDue })
            {
                scenarioOut.Add((vatDue, invoice.Vat));
                effects.Add(new WhatIfEffectResponse(WhatIfEffectKindDto.VatPaid, vatDue, -invoice.Vat));
            }
        }

        WhatIfHireResponse? hire = null;
        if (scenario.Hire is { } person)
        {
            (DateOnly Date, decimal Amount)[] salaries = [.. Salaries(person, tomorrow, horizon)];
            scenarioOut.AddRange(salaries);
            effects.AddRange(salaries.Select(s => new WhatIfEffectResponse(WhatIfEffectKindDto.SalaryPaid, s.Date, -s.Amount)));
            decimal monthly = MonthlyCost(person);
            hire = new WhatIfHireResponse(monthly, salaries.Sum(s => s.Amount), monthly * 12m);
        }

        decimal gap = 0m;
        WhatIfWeekResponse[] weeks =
        [
            .. Enumerable.Range(0, WhatIfRequest.Weeks).Select(week =>
            {
                DateOnly start = firstWeek.AddDays(7 * week);
                bool InWeek((DateOnly Date, decimal Amount) m) => m.Date >= start && m.Date < start.AddDays(7);
                decimal before = baseline.Where(InWeek).Sum(m => m.Amount);
                decimal after = scenarioIn.Where(InWeek).Sum(m => m.Amount);
                decimal outflow = scenarioOut.Where(InWeek).Sum(m => m.Amount);
                gap += after - before - outflow;
                return new WhatIfWeekResponse(start, before, after, outflow, gap);
            }),
        ];

        return new WhatIfResponse(
            today,
            settings.VatPeriodicity,
            vat,
            weeks.Sum(w => w.BaselineIn),
            weeks.Sum(w => w.ScenarioIn),
            weeks.Sum(w => w.ScenarioOut),
            gap,
            weeks,
            [.. effects.OrderBy(e => e.Date).ThenBy(e => e.Kind)],
            invoice,
            hire);
    }

    private static WhatIfInvoiceResponse Invoice(WhatIfInvoiceRequest trial, WhatIfVatDto vat, FiscalSettings settings, int extraDelayDays, DateOnly tomorrow)
    {
        decimal amount = Math.Round(trial.AmountExclTax, 2, MidpointRounding.AwayFromZero);
        decimal tax = vat == WhatIfVatDto.Franchise ? 0m : Math.Round(amount * trial.VatRatePercent / 100m, 2, MidpointRounding.AwayFromZero);
        DateOnly month = new(trial.IssueDate.Year, trial.IssueDate.Month, 1);
        DateOnly? periodStart = null;
        DateOnly? periodEnd = null;
        DateOnly? vatDue = null;
        if (vat != WhatIfVatDto.Franchise)
        {
            int length = vat == WhatIfVatDto.Quarterly ? 3 : 1;
            DateOnly start = month.AddMonths(-((month.Month - 1) % length));
            DateOnly end = start.AddMonths(length).AddDays(-1);
            (periodStart, periodEnd, vatDue) = (start, end, FiscalCalendar.VatReturnDueDate(settings, false, end));
        }

        DateOnly collection = trial.IssueDate.AddDays(trial.PaymentTermDays + extraDelayDays);
        return new WhatIfInvoiceResponse(
            amount,
            tax,
            amount + tax,
            month,
            settings.FiscalYearOf(trial.IssueDate).End,
            periodStart,
            periodEnd,
            vatDue,
            collection < tomorrow ? tomorrow : collection);
    }

    private static decimal MonthlyCost(WhatIfHireRequest hire) =>
        Math.Round(hire.MonthlyGrossSalary * (1m + (hire.EmployerChargesPercent / 100m)), 2, MidpointRounding.AwayFromZero);

    /// <summary>Les salaires chargés payés le dernier jour de chaque mois, de demain à la fin des semaines simulées.</summary>
    private static IEnumerable<(DateOnly Date, decimal Amount)> Salaries(WhatIfHireRequest hire, DateOnly from, DateOnly horizon)
    {
        decimal monthly = MonthlyCost(hire);
        for (DateOnly month = new(hire.StartDate.Year, hire.StartDate.Month, 1); month < horizon; month = month.AddMonths(1))
        {
            DateOnly payday = month.AddMonths(1).AddDays(-1);
            if (payday < from || payday >= horizon)
            {
                continue;
            }

            int days = DateTime.DaysInMonth(month.Year, month.Month);
            decimal share = hire.StartDate > month ? (decimal)(payday.DayNumber - hire.StartDate.DayNumber + 1) / days : 1m;
            yield return (payday, Math.Round(monthly * share, 2, MidpointRounding.AwayFromZero));
        }
    }
}

internal sealed class SimulateWhatIfHandler(IPaymentHistoryReader reader, IFiscalStore fiscal, TimeProvider clock)
    : IQueryHandler<SimulateWhatIfQuery, Result<WhatIfResponse>>
{
    public async Task<Result<WhatIfResponse>> HandleAsync(SimulateWhatIfQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        DateOnly today = LegalCalendar.Today(clock);
        IReadOnlyList<string> invalid = WhatIfSimulator.Invalid(query.Scenario, today);
        if (invalid.Count > 0)
        {
            return Result<WhatIfResponse>.Failure([.. invalid.Select(field => Error.Validation(ErrorCodes.Validation.Invalid, field))]);
        }

        IReadOnlyList<PaymentHistoryInvoice> invoices = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        FiscalSettings settings = await fiscal.FindSettingsAsync(cancellationToken).ConfigureAwait(false) ?? FiscalSettings.Default;
        IEnumerable<ExpectedCollectionResponse> expected = PaymentWeatherForecast.Compute(invoices, today).Customers.SelectMany(c => c.Expected);
        return WhatIfSimulator.Simulate(expected, settings, query.Scenario, today);
    }
}
