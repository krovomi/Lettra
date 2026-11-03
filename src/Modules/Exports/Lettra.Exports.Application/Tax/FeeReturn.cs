using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Tax;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Banking;

namespace Lettra.Exports.Application.Tax;

/// <summary>Un fournisseur du dossier tel que la déclaration des honoraires le lit, avec son suivi s'il en a un.</summary>
public sealed record FeePayee(SupplierId Id, string Name, string? Siret, string Address, string Country, string? Iban, FeeBeneficiary? Beneficiary);

/// <summary>Un virement sortant d'un relevé, reconnu par l'empreinte de l'IBAN du destinataire : sa date et son montant (positif).</summary>
public sealed record IbanDebit(string IbanHash, DateOnly Date, decimal Amount);

/// <summary>Les sommes versées à un bénéficiaire suivi : les virements des relevés, datés, et les sommes notées hors relevés pour l'exercice.</summary>
public sealed record FeePayments(FeePayee Payee, IReadOnlyList<IbanDebit> Debits, decimal PaidOutside);

/// <summary>Bénéficiaires suivis pour la déclaration des honoraires, fournisseurs du dossier et virements des relevés.</summary>
public interface IFeeReturnStore
{
    /// <summary>Les fournisseurs non archivés, avec leur suivi éventuel.</summary>
    Task<IReadOnlyList<FeePayee>> ListPayeesAsync(CancellationToken cancellationToken);

    Task<FeeBeneficiary?> FindAsync(SupplierId supplier, CancellationToken cancellationToken);

    Task AddAsync(FeeBeneficiary beneficiary, CancellationToken cancellationToken);

    Task RemoveAsync(FeeBeneficiary beneficiary, CancellationToken cancellationToken);

    /// <summary>Au moins un bénéficiaire suivi : le calendrier fiscal montre alors la DAS 2.</summary>
    Task<bool> AnyBeneficiaryAsync(CancellationToken cancellationToken);

    /// <summary>Les virements sortants des relevés du <paramref name="from"/> au <paramref name="to"/> vers ces empreintes d'IBAN, datés (montants positifs).</summary>
    Task<IReadOnlyList<IbanDebit>> DebitsAsync(IReadOnlyCollection<string> ibanHashes, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>La date de la dernière opération des relevés importés, ou <c>null</c> sans relevé.</summary>
    Task<DateOnly?> LastBankLineAsync(CancellationToken cancellationToken);
}

/// <summary>La déclaration des honoraires de l'exercice qui contient <paramref name="Date"/> (par défaut, le dernier exercice clos).</summary>
public sealed record GetFeeReturnQuery(DossierId Dossier, DateOnly? Date = null) : IQuery<Result<FeeReturnResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Suivre un fournisseur pour la déclaration des honoraires, ou mettre à jour son suivi : qui prépare les déclarations.</summary>
public sealed record SaveFeeBeneficiaryCommand(DossierId Dossier, SupplierId SupplierId, SaveFeeBeneficiaryRequest Request, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IVersionedCommand
{
    public Permission Permission => Permission.FecExporter;
}

/// <summary>Ne plus suivre un fournisseur pour la déclaration des honoraires (les sommes notées hors relevés partent avec).</summary>
public sealed record RemoveFeeBeneficiaryCommand(DossierId Dossier, SupplierId SupplierId, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IVersionedCommand
{
    public Permission Permission => Permission.FecExporter;
}

/// <summary>
/// La déclaration des honoraires (DAS 2) d'un exercice : pour chaque bénéficiaire suivi, les virements des relevés vers
/// son IBAN (reconnus par l'empreinte, les IBAN des relevés ne sont jamais gardés en clair) et les sommes notées hors
/// relevés ; au-delà du seuil de l'exercice (2 400 € TTC depuis 2024), il se déclare. L'échéance vient du calendrier fiscal.
/// </summary>
public sealed class FeeReturns(IFeeReturnStore store, IIbanFingerprints fingerprints, TaxDeclarations tax)
{
    public async Task<FeeReturnResponse> ReadAsync(DateOnly? date, DateOnly today, CancellationToken cancellationToken)
    {
        FiscalSettings settings = await tax.SettingsAsync(cancellationToken).ConfigureAwait(false);
        (DateOnly start, DateOnly end) = date is { } d ? settings.FiscalYearOf(d) : LastClosed(settings, today);
        IReadOnlyList<FeePayee> payees = await store.ListPayeesAsync(cancellationToken).ConfigureAwait(false);
        List<FeePayee> tracked = [.. payees.Where(p => p.Beneficiary is not null)];
        (Dictionary<SupplierId, string> hashes, IReadOnlyList<IbanDebit> debits) = await DebitsAsync(tracked, start, end, cancellationToken).ConfigureAwait(false);
        Dictionary<string, decimal> paid = debits.GroupBy(d => d.IbanHash, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Sum(d => d.Amount), StringComparer.Ordinal);
        FiscalDeadlineResponse? deadline = tracked.Count == 0 ? null : await DeadlineAsync(start, end, today, cancellationToken).ConfigureAwait(false);
        return Build(
            start,
            end,
            deadline,
            payees,
            p => hashes.TryGetValue(p.Id, out string? hash) ? paid.GetValueOrDefault(hash) : 0m,
            await store.LastBankLineAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Les sommes versées à chaque bénéficiaire suivi : virements des relevés datés du <paramref name="from"/> au
    /// <paramref name="to"/>, et sommes notées hors relevés pour l'exercice qui commence le <paramref name="fiscalYearStart"/>.
    /// Les seuils qui approchent s'en servent pour projeter le total de l'exercice.
    /// </summary>
    public async Task<IReadOnlyList<FeePayments>> PaymentsAsync(DateOnly from, DateOnly to, DateOnly fiscalYearStart, CancellationToken cancellationToken)
    {
        List<FeePayee> tracked = [.. (await store.ListPayeesAsync(cancellationToken).ConfigureAwait(false)).Where(p => p.Beneficiary is not null)];
        (Dictionary<SupplierId, string> hashes, IReadOnlyList<IbanDebit> debits) = await DebitsAsync(tracked, from, to, cancellationToken).ConfigureAwait(false);
        return
        [
            .. tracked.Select(p => new FeePayments(
                p,
                hashes.TryGetValue(p.Id, out string? hash) ? [.. debits.Where(d => string.Equals(d.IbanHash, hash, StringComparison.Ordinal))] : [],
                p.Beneficiary!.PaidOutsideFor(fiscalYearStart))),
        ];
    }

    /// <summary>L'exercice demandé n'en est un que si son premier jour suit une clôture.</summary>
    public async Task<bool> IsFiscalYearStartAsync(DateOnly start, CancellationToken cancellationToken) =>
        (await tax.SettingsAsync(cancellationToken).ConfigureAwait(false)).FiscalYearOf(start).Start == start;

    /// <summary>Le dernier exercice clos à la date du jour : celui qui précède l'exercice en cours.</summary>
    public static (DateOnly Start, DateOnly End) LastClosed(FiscalSettings settings, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(settings);
        (DateOnly current, _) = settings.FiscalYearOf(today);
        return settings.FiscalYearOf(current.AddDays(-1));
    }

    public static FeeReturnResponse Build(
        DateOnly start, DateOnly end, FiscalDeadlineResponse? deadline, IReadOnlyList<FeePayee> payees, Func<FeePayee, decimal> paidByBank, DateOnly? bankLinesUntil)
    {
        ArgumentNullException.ThrowIfNull(payees);
        ArgumentNullException.ThrowIfNull(paidByBank);
        return new FeeReturnResponse(
            start,
            end,
            deadline,
            FeeBeneficiary.ThresholdFor(end),
            [
                .. payees.Where(p => p.Beneficiary is not null)
                    .Select(p => Row(p, paidByBank(p), start, end))
                    .OrderByDescending(r => r.Total)
                    .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
            ],
            [
                .. payees.Where(p => p.Beneficiary is null)
                    .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(p => new FeeReturnCandidate(p.Id.Value, p.Name)),
            ],
            bankLinesUntil);
    }

    public static FeeBeneficiaryRow Row(FeePayee payee, decimal paidByBank, DateOnly fiscalYearStart, DateOnly fiscalYearEnd)
    {
        ArgumentNullException.ThrowIfNull(payee);
        FeeBeneficiary beneficiary = payee.Beneficiary ?? throw new ArgumentException("Bénéficiaire non suivi.", nameof(payee));
        decimal outside = beneficiary.PaidOutsideFor(fiscalYearStart);
        decimal total = paidByBank + outside;
        return new FeeBeneficiaryRow(
            payee.Id.Value,
            payee.Name,
            payee.Siret,
            payee.Address,
            (FeeNatureDto)(int)beneficiary.Nature,
            beneficiary.Profession,
            paidByBank,
            outside,
            total,
            FeeBeneficiary.MustBeDeclared(total, fiscalYearEnd),
            payee.Siret is null && string.Equals(payee.Country, "FR", StringComparison.Ordinal),
            payee.Iban is not null,
            beneficiary.Version);
    }

    /// <summary>
    /// L'indicateur de la vue cabinet : la DAS 2 du dernier exercice clos, pas encore marquée déposée, avec des bénéficiaires
    /// à déclarer. Pour information tant que l'échéance est loin, à traiter dans les trente jours, critique en retard.
    /// </summary>
    public static DossierIndicator? Indicator(FeeReturnResponse view, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(view);
        int toDeclare = view.Rows.Count(r => r.ToDeclare);
        if (toDeclare == 0 || view.Deadline is not { } deadline
            || deadline.State is not (FiscalDeadlineStateDto.Upcoming or FiscalDeadlineStateDto.DueSoon or FiscalDeadlineStateDto.Overdue))
        {
            return null;
        }

        IndicatorLevel level = deadline.State == FiscalDeadlineStateDto.Overdue ? IndicatorLevel.Critical
            : deadline.DueDate.DayNumber - today.DayNumber <= 30 ? IndicatorLevel.Warning
            : IndicatorLevel.Info;
        return new DossierIndicator(DossierIndicatorKeys.FeeReturn, toDeclare, level, deadline.DueDate);
    }

    /// <summary>Les empreintes d'IBAN des bénéficiaires suivis, et les virements des relevés vers elles sur la période.</summary>
    private async Task<(Dictionary<SupplierId, string> Hashes, IReadOnlyList<IbanDebit> Debits)> DebitsAsync(
        IReadOnlyList<FeePayee> tracked, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        Dictionary<SupplierId, string> hashes = [];
        foreach (FeePayee payee in tracked)
        {
            if (await fingerprints.FingerprintAsync(payee.Iban, cancellationToken).ConfigureAwait(false) is { } hash)
            {
                hashes[payee.Id] = hash;
            }
        }

        IReadOnlyList<IbanDebit> debits = hashes.Count == 0
            ? []
            : await store.DebitsAsync([.. hashes.Values.Distinct(StringComparer.Ordinal)], from, to, cancellationToken).ConfigureAwait(false);
        return (hashes, debits);
    }

    private async Task<FiscalDeadlineResponse?> DeadlineAsync(DateOnly start, DateOnly end, DateOnly today, CancellationToken cancellationToken)
    {
        Result<FiscalCalendarResponse> calendar = await tax.CalendarAsync(start, end.AddMonths(6), today, cancellationToken).ConfigureAwait(false);
        return calendar.IsSuccess
            ? calendar.Value.Deadlines.FirstOrDefault(d => d.Obligation == FiscalObligationDto.FeeReturnDas2 && d.PeriodStart == start)
            : null;
    }
}

internal sealed class FeeReturnHandlers(FeeReturns returns, IFeeReturnStore store, ISupplierRepository suppliers, IUnitOfWork unitOfWork, TimeProvider clock) :
    IQueryHandler<GetFeeReturnQuery, Result<FeeReturnResponse>>,
    ICommandHandler<SaveFeeBeneficiaryCommand, Result>,
    ICommandHandler<RemoveFeeBeneficiaryCommand, Result>
{
    public async Task<Result<FeeReturnResponse>> HandleAsync(GetFeeReturnQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await returns.ReadAsync(query.Date, LegalCalendar.Today(clock), cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result> HandleAsync(SaveFeeBeneficiaryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        SaveFeeBeneficiaryRequest request = command.Request ?? throw new ArgumentException("Demande vide.", nameof(command));
        if (await suppliers.FindAsync(command.SupplierId, cancellationToken).ConfigureAwait(false) is not { IsArchived: false })
        {
            return FeeErrors.SupplierNotFound;
        }

        if (!await returns.IsFiscalYearStartAsync(request.FiscalYearStart, cancellationToken).ConfigureAwait(false))
        {
            return FeeErrors.YearInvalid;
        }

        FeeBeneficiary.Draft draft = new(
            (FeeNature)(int)request.Nature, request.Profession, new FeeBeneficiary.YearAmount(request.FiscalYearStart, request.PaidOutside));
        DateTimeOffset now = clock.GetUtcNow();
        FeeBeneficiary? beneficiary = await store.FindAsync(command.SupplierId, cancellationToken).ConfigureAwait(false);
        if (beneficiary is null)
        {
            Result<FeeBeneficiary> created = FeeBeneficiary.Create(command.SupplierId, draft, now);
            if (created.IsFailure)
            {
                return Result.Failure(created.Errors);
            }

            await store.AddAsync(created.Value, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            Result updated = Concurrency.Check(beneficiary, command.ExpectedVersion);
            if (updated.IsSuccess)
            {
                updated = beneficiary.Update(draft, now);
            }

            if (updated.IsFailure)
            {
                return updated;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> HandleAsync(RemoveFeeBeneficiaryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await store.FindAsync(command.SupplierId, cancellationToken).ConfigureAwait(false) is not { } beneficiary)
        {
            return FeeErrors.BeneficiaryNotFound;
        }

        Result fresh = Concurrency.Check(beneficiary, command.ExpectedVersion);
        if (fresh.IsFailure)
        {
            return fresh;
        }

        await store.RemoveAsync(beneficiary, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>« Honoraires à déclarer » pour la vue cabinet ; voir <see cref="FeeReturns.Indicator"/>.</summary>
internal sealed class FeeReturnIndicatorSource(FeeReturns returns) : IDossierIndicatorSource
{
    public async Task<IReadOnlyList<DossierIndicator>> ComputeAsync(DateOnly today, CancellationToken cancellationToken) =>
        FeeReturns.Indicator(await returns.ReadAsync(null, today, cancellationToken).ConfigureAwait(false), today) is { } indicator ? [indicator] : [];
}
