using Lettra.Contracts.Tax;
using Lettra.Domain.Billing;

namespace Lettra.Exports.Application.Tax;

/// <summary>Ce dont le calendrier a besoin en plus des réglages : régime, mois avec une DES, TVA des exercices passés, dépôts.</summary>
public sealed record FiscalCalendarInput(
    FiscalSettings Settings,
    VatDeclarationRegime? Regime,
    bool VatFranchise,
    DateOnly Today,
    IReadOnlySet<DateOnly> DesMonths,
    IReadOnlyDictionary<DateOnly, decimal> VatByFiscalYearEnd,
    IReadOnlyList<FiscalFiling> Filings);

/// <summary>
/// Calendrier fiscal d'un dossier, calculé à partir du régime de TVA et des réglages (annexe Paperasse
/// <c>comptable/references/calendar.md</c>). Fonction pure. Règles retenues, <b>à faire relire</b> (§13) :
/// <list type="bullet">
/// <item>CA3 : le jour limite des réglages (15 par défaut, la date la plus tôt de la fourchette du 15 au 24) du mois qui suit le mois ou le trimestre ;</item>
/// <item>CA12 : le deuxième jour ouvré qui suit le 1er mai pour un exercice civil, trois mois après la clôture sinon ;
/// acomptes au jour limite du 7e et du 12e mois de l'exercice (juillet et décembre), 55 % et 40 % de la TVA de l'exercice
/// précédent, dispensés sous 1 000 € ;</item>
/// <item>DES : le 10e jour ouvrable du mois qui suit, pour un mois qui a des prestations intracommunautaires ;</item>
/// <item>CFE : le 15 décembre ; IS : acomptes le 15 du 3e, 6e, 9e et 12e mois de l'exercice, solde le 15 mai (exercice
/// civil) ou le 15 du 4e mois après la clôture, liasse comme la CA12.</item>
/// </list>
/// Une date limite qui tombe un jour non ouvré n'est pas reportée : Lettra rappelle la date la plus tôt.
/// </summary>
public static class FiscalCalendar
{
    /// <summary>Une échéance « proche » arrive dans ce nombre de jours (centre d'alertes, vue cabinet).</summary>
    public const int DueSoonDays = 7;

    /// <summary>Sous ce montant de TVA due l'exercice précédent, pas d'acompte de CA12 (art. 287-3 du CGI).</summary>
    public const decimal InstallmentThreshold = 1000m;

    public static IReadOnlyList<FiscalDeadlineResponse> Compute(FiscalCalendarInput input, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(input);
        FiscalSettings settings = input.Settings;
        List<(FiscalObligationDto Obligation, DateOnly Start, DateOnly End, DateOnly Due, bool Exempt, string? Note)> items = [];

        for (int year = from.Year - 1; year <= to.Year + 1; year++)
        {
            if (!input.VatFranchise && input.Regime is VatDeclarationRegime.RealNormal or VatDeclarationRegime.MiniReal)
            {
                bool quarterly = settings.VatPeriodicity == VatPeriodicityDto.Quarterly;
                for (int month = 1; month <= 12; month += quarterly ? 3 : 1)
                {
                    DateOnly start = new(year, month, 1);
                    DateOnly end = start.AddMonths(quarterly ? 3 : 1).AddDays(-1);
                    items.Add((FiscalObligationDto.VatReturnCa3, start, end, DayOf(end.AddDays(1), settings.VatDeadlineDay), false, "VatDeadlineDay"));
                }
            }

            (DateOnly fyStart, DateOnly fyEnd) = (settings.YearEnd(year - 1).AddDays(1), settings.YearEnd(year));
            DateOnly declarationDue = settings.IsCalendarYear ? FrenchCalendar.WorkingDayAfter(new DateOnly(year + 1, 5, 1), 2) : fyEnd.AddMonths(3);
            if (!input.VatFranchise && input.Regime == VatDeclarationRegime.RealSimplified)
            {
                items.Add((FiscalObligationDto.VatReturnCa12, fyStart, fyEnd, declarationDue, false, settings.IsCalendarYear ? "Ca12" : "Ca12NonCalendar"));
                (decimal? previous, bool exempt) = PreviousYearVat(input, fyStart);
                for (int number = 1; number <= 2; number++)
                {
                    DateOnly start = fyStart.AddMonths(number == 1 ? 0 : 6);
                    DateOnly month = fyStart.AddMonths(number == 1 ? 6 : 11);
                    string note = previous is null ? "InstallmentUnknown" : exempt ? "InstallmentExempt" : number == 1 ? "Installment55" : "Installment40";
                    items.Add((FiscalObligationDto.VatInstallment, start, number == 1 ? fyStart.AddMonths(6).AddDays(-1) : fyEnd,
                        DayOf(month, settings.VatDeadlineDay), exempt, note));
                }
            }

            if (settings.Cfe)
            {
                items.Add((FiscalObligationDto.Cfe, new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), new DateOnly(year, 12, 15), false, null));
            }

            if (settings.CorporateIncomeTax)
            {
                for (int quarter = 0; quarter < 4; quarter++)
                {
                    DateOnly start = fyStart.AddMonths(3 * quarter);
                    DateOnly month = fyStart.AddMonths((3 * quarter) + 2);
                    items.Add((FiscalObligationDto.CorporateTaxInstallment, start, start.AddMonths(3).AddDays(-1), new DateOnly(month.Year, month.Month, 15), false, "CorporateTaxThreshold"));
                }

                DateOnly balanceMonth = settings.IsCalendarYear ? new DateOnly(year + 1, 5, 1) : fyEnd.AddDays(1).AddMonths(3);
                items.Add((FiscalObligationDto.CorporateTaxBalance, fyStart, fyEnd, new DateOnly(balanceMonth.Year, balanceMonth.Month, 15), false, null));
                items.Add((FiscalObligationDto.TaxReturn, fyStart, fyEnd, declarationDue, false, null));
            }
        }

        if (!input.VatFranchise)
        {
            foreach (DateOnly month in input.DesMonths)
            {
                DateOnly start = new(month.Year, month.Month, 1);
                items.Add((FiscalObligationDto.Des, start, start.AddMonths(1).AddDays(-1), FrenchCalendar.OuvrableDayOfMonth(start.AddMonths(1), 10), false, "Des"));
            }
        }

        Dictionary<(FiscalObligationDto, DateOnly), FiscalFiling> filings = input.Filings
            .GroupBy(f => (f.Obligation, f.PeriodStart))
            .ToDictionary(g => g.Key, g => g.Last());

        return items
            .Where(i => i.Due >= from && i.Due <= to)
            .Select(i =>
            {
                FiscalFiling? filing = filings.GetValueOrDefault((i.Obligation, i.Start));
                return new FiscalDeadlineResponse(i.Obligation, i.Start, i.End, i.Due, StateOf(i.Due, i.Exempt, filing, input), filing?.FiledAt, i.Note);
            })
            .OrderBy(d => d.DueDate)
            .ThenBy(d => d.Obligation)
            .ToList();
    }

    /// <summary>
    /// TVA due de l'exercice qui précède celui commençant le <paramref name="fiscalYearStart"/>, et si les acomptes en sont
    /// dispensés. <c>null</c> quand Lettra n'a aucune pièce sur cet exercice.
    /// </summary>
    public static (decimal? Amount, bool Exempt) PreviousYearVat(FiscalCalendarInput input, DateOnly fiscalYearStart)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.VatByFiscalYearEnd.TryGetValue(fiscalYearStart.AddDays(-1), out decimal amount)
            ? (amount, amount < InstallmentThreshold)
            : (null, false);
    }

    /// <summary>Acomptes de CA12 de l'exercice : 55 % puis 40 % de la TVA de l'exercice précédent, arrondis à l'euro.</summary>
    public static IReadOnlyList<VatInstallmentResponse> Installments(FiscalCalendarInput input, DateOnly fiscalYearStart)
    {
        ArgumentNullException.ThrowIfNull(input);
        (decimal? previous, bool exempt) = PreviousYearVat(input, fiscalYearStart);
        decimal basis = previous ?? 0m;
        return
        [
            new(1, DayOf(fiscalYearStart.AddMonths(6), input.Settings.VatDeadlineDay), exempt ? 0m : Math.Round(basis * 0.55m, 0, MidpointRounding.AwayFromZero), exempt),
            new(2, DayOf(fiscalYearStart.AddMonths(11), input.Settings.VatDeadlineDay), exempt ? 0m : Math.Round(basis * 0.40m, 0, MidpointRounding.AwayFromZero), exempt),
        ];
    }

    /// <summary>Échéance de la déclaration de TVA d'une période (la CA3 du mois ou du trimestre, la CA12 de l'exercice).</summary>
    public static DateOnly VatReturnDueDate(FiscalSettings settings, bool ca12, DateOnly periodEnd)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!ca12)
        {
            return DayOf(periodEnd.AddDays(1), settings.VatDeadlineDay);
        }

        return settings.IsCalendarYear ? FrenchCalendar.WorkingDayAfter(new DateOnly(periodEnd.Year + 1, 5, 1), 2) : periodEnd.AddMonths(3);
    }

    /// <summary>Échéance de la DES d'un mois : le 10e jour ouvrable du mois suivant.</summary>
    public static DateOnly DesDueDate(int year, int month) => FrenchCalendar.OuvrableDayOfMonth(new DateOnly(year, month, 1).AddMonths(1), 10);

    private static FiscalDeadlineStateDto StateOf(DateOnly due, bool exempt, FiscalFiling? filing, FiscalCalendarInput input)
    {
        if (filing is not null)
        {
            return FiscalDeadlineStateDto.Filed;
        }

        if (exempt)
        {
            return FiscalDeadlineStateDto.Exempt;
        }

        if (due < input.Today)
        {
            return input.Settings.TrackedFrom is { } tracked && due >= tracked ? FiscalDeadlineStateDto.Overdue : FiscalDeadlineStateDto.Past;
        }

        return due <= input.Today.AddDays(DueSoonDays) ? FiscalDeadlineStateDto.DueSoon : FiscalDeadlineStateDto.Upcoming;
    }

    private static DateOnly DayOf(DateOnly month, int day) => new(month.Year, month.Month, Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)));
}
