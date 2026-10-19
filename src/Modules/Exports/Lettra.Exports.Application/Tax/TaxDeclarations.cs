using Lettra.Application.Abstractions.Persistence;
using Lettra.Calculations;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Tax;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;

namespace Lettra.Exports.Application.Tax;

/// <summary>Refus des déclarations fiscales.</summary>
public static class TaxErrors
{
    public static Error ProfileMissing => Error.Validation(ErrorCodes.Tax.ProfileMissing);

    public static Error VatFranchise => Error.Validation(ErrorCodes.Tax.VatFranchise);

    public static Error RegimeMissing => Error.Validation(ErrorCodes.Tax.RegimeMissing);

    public static Error PeriodInvalid => Error.Validation(ErrorCodes.Tax.PeriodInvalid, "Date");

    public static Error FiscalYearEndInvalid => Error.Validation(ErrorCodes.Tax.FiscalYearEndInvalid, "FiscalYearEndDay");

    public static Error DeadlineDayInvalid => Error.Validation(ErrorCodes.Tax.DeadlineDayInvalid, "VatDeadlineDay");

    public static Error DeadlineUnknown => Error.Validation(ErrorCodes.Tax.DeadlineUnknown, "PeriodStart");

    public static Error VersionMismatch => Error.Conflict(ErrorCodes.Concurrency.VersionMismatch);
}

/// <summary>
/// Brouillons de déclarations et calendrier fiscal d'un dossier, partagés par les requêtes des utilisateurs, la vue
/// cabinet et la passe d'alertes du Worker (qui agit dans la portée du dossier, sans droits d'utilisateur). Tout est lu
/// dans les pièces validées et le journal des ventes : rien n'est déposé nulle part.
/// </summary>
public sealed class TaxDeclarations(
    ICompanyProfileRepository profiles,
    ITaxDocumentReader documents,
    ISalesJournalReader journal,
    IFiscalStore store,
    IVatOnReceiptsSource receipts)
{
    /// <summary>Les réglages enregistrés, ou ceux par défaut.</summary>
    public async Task<FiscalSettings> SettingsAsync(CancellationToken cancellationToken) =>
        await store.FindSettingsAsync(cancellationToken).ConfigureAwait(false) ?? FiscalSettings.Default;

    /// <summary>Période de déclaration qui contient <paramref name="date"/> : le mois, le trimestre civil ou l'exercice.</summary>
    public static (DateOnly From, DateOnly To) PeriodOf(VatReturnFormDto form, FiscalSettings settings, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (form == VatReturnFormDto.Ca12)
        {
            return settings.FiscalYearOf(date);
        }

        int months = settings.VatPeriodicity == VatPeriodicityDto.Quarterly ? 3 : 1;
        DateOnly start = new(date.Year, ((date.Month - 1) / months * months) + 1, 1);
        return (start, start.AddMonths(months).AddDays(-1));
    }

    public async Task<Result<VatReturnResponse>> VatReturnAsync(DateOnly date, CancellationToken cancellationToken)
    {
        CompanyProfile? profile = await profiles.FindAsync(cancellationToken).ConfigureAwait(false);
        Result<VatDeclarationRegime> regime = RegimeOf(profile);
        if (regime.IsFailure)
        {
            return Result<VatReturnResponse>.Failure(regime.Errors);
        }

        FiscalSettings settings = await SettingsAsync(cancellationToken).ConfigureAwait(false);
        VatReturnFormDto form = regime.Value == VatDeclarationRegime.RealSimplified ? VatReturnFormDto.Ca12 : VatReturnFormDto.Ca3;
        (DateOnly from, DateOnly to) = PeriodOf(form, settings, date);

        (IReadOnlyList<VatOperation> operations, IReadOnlyList<TaxDocument> pieces) = await OperationsAsync(from, to, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<SalesJournalExportEntry> entries = await journal.ReadAsync(from, to, cancellationToken).ConfigureAwait(false);
        decimal journalVat = entries.SelectMany(e => e.Lines).Where(l => l.Account == SalesAccounts.VatCollected).Sum(l => l.Credit - l.Debit);
        IReadOnlyList<string> drafts = await documents.ListDraftsAsync(new DateTimeOffset(to.AddDays(1), TimeOnly.MinValue, TimeSpan.Zero), cancellationToken).ConfigureAwait(false);
        List<string> notTransmitted = [.. pieces.Where(MustBeTransmitted).Where(p => !p.Transmitted).Select(p => p.Number)];

        IReadOnlyList<VatInstallmentResponse> installments = [];
        if (form == VatReturnFormDto.Ca12)
        {
            FiscalCalendarInput input = await CalendarInputAsync(profile!, settings, from, from, new HashSet<DateOnly>(), cancellationToken).ConfigureAwait(false);
            installments = FiscalCalendar.Installments(input, from);
        }

        FiscalObligationDto obligation = form == VatReturnFormDto.Ca12 ? FiscalObligationDto.VatReturnCa12 : FiscalObligationDto.VatReturnCa3;
        IReadOnlyList<FiscalFiling> filings = await store.ListFilingsAsync(from, from, cancellationToken).ConfigureAwait(false);
        DateTimeOffset? filedAt = filings.FirstOrDefault(f => f.Obligation == obligation && f.PeriodStart == from)?.FiledAt;

        return VatReturnCalculator.Build(operations, new VatReturnContext(
            form,
            (VatDeclarationRegimeDto)(int)regime.Value,
            from,
            to,
            FiscalCalendar.VatReturnDueDate(settings, form == VatReturnFormDto.Ca12, to),
            journalVat,
            drafts,
            notTransmitted,
            receipts.IsActive,
            installments,
            filedAt));
    }

    public async Task<Result<DesResponse>> DesAsync(int year, int month, CancellationToken cancellationToken)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
        {
            return TaxErrors.PeriodInvalid;
        }

        CompanyProfile? profile = await profiles.FindAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return TaxErrors.ProfileMissing;
        }

        DateOnly from = new(year, month, 1);
        DateOnly to = from.AddMonths(1).AddDays(-1);
        IReadOnlyList<TaxDocument> pieces = await documents.ReadAsync(from, to, cancellationToken).ConfigureAwait(false);
        List<VatOperation> operations = [.. pieces.SelectMany(VatOperations.Of)];
        IReadOnlyList<FiscalFiling> filings = await store.ListFilingsAsync(from, from, cancellationToken).ConfigureAwait(false);
        DateTimeOffset? filedAt = filings.FirstOrDefault(f => f.Obligation == FiscalObligationDto.Des && f.PeriodStart == from)?.FiledAt;
        return DesCalculator.Build(year, month, FiscalCalendar.DesDueDate(year, month), operations, filedAt);
    }

    public async Task<Result<FiscalCalendarResponse>> CalendarAsync(DateOnly from, DateOnly to, DateOnly today, CancellationToken cancellationToken)
    {
        if (to < from || to > from.AddMonths(24))
        {
            return TaxErrors.PeriodInvalid;
        }

        CompanyProfile? profile = await profiles.FindAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return TaxErrors.ProfileMissing;
        }

        FiscalSettings settings = await SettingsAsync(cancellationToken).ConfigureAwait(false);

        // Les mois avec une DES : ceux de la fenêtre, plus le mois précédent (son échéance tombe dans la fenêtre).
        DateOnly desFrom = new DateOnly(from.Year, from.Month, 1).AddMonths(-1);
        DateOnly desTo = to < today ? to : today;
        IReadOnlyList<TaxDocument> recent = desTo < desFrom ? [] : await documents.ReadAsync(desFrom, desTo, cancellationToken).ConfigureAwait(false);
        HashSet<DateOnly> desMonths = [.. recent
            .SelectMany(VatOperations.Of)
            .Where(o => DesCalculator.IsIntraEuService(o) && DesCalculator.OtherMemberStates.Contains(o.CustomerCountry))
            .Select(o => new DateOnly(o.Date.Year, o.Date.Month, 1))];

        FiscalCalendarInput input = await CalendarInputAsync(profile, settings, from, today, desMonths, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<FiscalFiling> filings = await store.ListFilingsAsync(from.AddYears(-2), to, cancellationToken).ConfigureAwait(false);
        input = input with { Filings = filings };
        return new FiscalCalendarResponse(
            from,
            to,
            today,
            profile.VatDeclarationRegime is { } regime ? (VatDeclarationRegimeDto)(int)regime : null,
            profile.VatFranchise,
            settings.ToResponse(),
            FiscalCalendar.Compute(input, from, to));
    }

    /// <summary>
    /// Échéances à surveiller : en retard, ou dues d'ici <paramref name="days"/> jours, pas encore marquées déposées.
    /// C'est la requête de la vue cabinet (« TVA à déclarer dans les 7 jours ») et de la passe d'alertes.
    /// </summary>
    public async Task<Result<IReadOnlyList<FiscalDeadlineResponse>>> UpcomingAsync(DateOnly today, int days, CancellationToken cancellationToken)
    {
        Result<FiscalCalendarResponse> calendar = await CalendarAsync(today.AddMonths(-3), today.AddDays(Math.Clamp(days, 0, 366)), today, cancellationToken).ConfigureAwait(false);
        if (calendar.IsFailure)
        {
            return Result<IReadOnlyList<FiscalDeadlineResponse>>.Failure(calendar.Errors);
        }

        return calendar.Value.Deadlines
            .Where(d => d.State is FiscalDeadlineStateDto.Overdue || (d.State is FiscalDeadlineStateDto.DueSoon or FiscalDeadlineStateDto.Upcoming && d.DueDate >= today))
            .ToList();
    }

    private async Task<FiscalCalendarInput> CalendarInputAsync(
        CompanyProfile profile, FiscalSettings settings, DateOnly from, DateOnly today, IReadOnlySet<DateOnly> desMonths, CancellationToken cancellationToken)
    {
        // TVA due des deux exercices qui précèdent la fenêtre, pour les acomptes de CA12 (ventes seules : la TVA
        // déductible arrive avec les achats).
        Dictionary<DateOnly, decimal> vatByYearEnd = [];
        if (profile.VatDeclarationRegime == VatDeclarationRegime.RealSimplified)
        {
            (DateOnly start, _) = settings.FiscalYearOf(from);
            for (int back = 1; back <= 2; back++)
            {
                (DateOnly fyStart, DateOnly fyEnd) = settings.FiscalYearOf(start.AddYears(-back));
                IReadOnlyList<TaxDocument> pieces = await documents.ReadAsync(fyStart, fyEnd, cancellationToken).ConfigureAwait(false);
                if (pieces.Count > 0)
                {
                    vatByYearEnd[fyEnd] = Math.Max(0m, pieces.SelectMany(VatOperations.Of).Sum(o => o.VatAmount));
                }
            }
        }

        return new FiscalCalendarInput(settings, profile.VatDeclarationRegime, profile.VatFranchise, today, desMonths, vatByYearEnd, []);
    }

    private async Task<(IReadOnlyList<VatOperation> Operations, IReadOnlyList<TaxDocument> Pieces)> OperationsAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        IReadOnlyList<TaxDocument> pieces = await documents.ReadAsync(from, to, cancellationToken).ConfigureAwait(false);
        List<VatOperation> operations = [.. pieces.SelectMany(VatOperations.Of)];
        if (!receipts.IsActive)
        {
            return (operations, pieces);
        }

        IReadOnlyList<VatReceiptAllocation> allocations = await receipts.ReadAsync(from, to, cancellationToken).ConfigureAwait(false);
        return (VatOperations.WithReceipts(operations, allocations, pieces.ToDictionary(p => p.InvoiceId)), pieces);
    }

    private static Result<VatDeclarationRegime> RegimeOf(CompanyProfile? profile) =>
        profile is null ? TaxErrors.ProfileMissing
        : profile.VatFranchise ? TaxErrors.VatFranchise
        : profile.VatDeclarationRegime is { } regime ? regime
        : TaxErrors.RegimeMissing;

    /// <summary>Facture électronique obligatoire : un client professionnel établi en France (identifié par son SIREN).</summary>
    private static bool MustBeTransmitted(TaxDocument piece) =>
        piece.Customer is { IsBusiness: true, CountryCode: "FR", Siren: not null };
}
