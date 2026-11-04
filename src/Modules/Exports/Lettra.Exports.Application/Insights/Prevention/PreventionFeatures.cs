using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Mail;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Kpi;
using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Tax;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Prevention;
using Lettra.Domain.SharedKernel;
using Lettra.Domain.Vigilance;
using Lettra.Exports.Application.Tax;

namespace Lettra.Exports.Application.Insights.Prevention;

/// <summary>Le radar de prévention du dossier : le niveau d'attention et chaque signal avec sa raison et ses chiffres.</summary>
public sealed record GetPreventionRadarQuery(DossierId Dossier) : IQuery<Result<PreventionRadarResponse>>, IRequirePermission
{
    public Permission Permission => Permission.PreventionConsulter;
}

/// <summary>
/// La carte « Signaux de prévention » de l'accueil du dossier. Filtrée par le droit : qui n'a pas
/// <see cref="Permission.PreventionConsulter"/> reçoit une carte cachée, pas un refus (l'accueil s'ouvre pour tous).
/// </summary>
public sealed record GetPreventionSummaryQuery(DossierId Dossier) : IQuery<Result<PreventionSummaryResponse>>, IFilterByPermission, IDossierScoped
{
    public Permission Permission => Permission.PreventionConsulter;
}

/// <summary>Régler les seuils du radar du dossier.</summary>
public sealed record UpdatePreventionSettingsCommand(DossierId Dossier, UpdatePreventionSettingsRequest Request)
    : ICommand<Result<PreventionSettingsResponse>>, IRequirePermission
{
    public Permission Permission => Permission.PreventionRegler;
}

/// <summary>Le courrier type de proposition de rendez-vous, préparé avec les sujets des signaux allumés.</summary>
public sealed record GetPreventionLetterQuery(DossierId Dossier, string? Language) : IQuery<Result<PreventionLetterResponse>>, IRequirePermission
{
    public Permission Permission => Permission.PreventionConsulter;
}

/// <summary>Envoyer par e-mail le courrier relu par le collaborateur.</summary>
public sealed record SendPreventionLetterCommand(DossierId Dossier, SendPreventionLetterRequest Request) : ICommand<Result>, IRequirePermission
{
    public Permission Permission => Permission.PreventionConsulter;
}

/// <summary>Codes d'erreur des cas d'usage du radar (ceux des seuils sont dans <see cref="PreventionErrors"/>).</summary>
public static class PreventionLetterErrors
{
    public static Error RecipientRequired => Error.Validation(ErrorCodes.Prevention.RecipientRequired, nameof(SendPreventionLetterRequest.To));

    public static Error SubjectRequired => Error.Validation(ErrorCodes.Prevention.SubjectRequired, nameof(SendPreventionLetterRequest.Subject));

    public static Error SubjectTooLong => Error.Validation(ErrorCodes.Prevention.SubjectTooLong, nameof(SendPreventionLetterRequest.Subject));

    public static Error BodyRequired => Error.Validation(ErrorCodes.Prevention.BodyRequired, nameof(SendPreventionLetterRequest.Body));

    public static Error BodyTooLong => Error.Validation(ErrorCodes.Prevention.BodyTooLong, nameof(SendPreventionLetterRequest.Body));
}

/// <summary>
/// Lit dans le dossier tout ce que le radar compare, puis applique les règles. Partagé par l'écran, la carte de
/// l'accueil et l'indicateur de la vue cabinet (qui tourne dans la portée du dossier, sans utilisateur).
/// </summary>
public sealed class PreventionRadarReader(
    IPreventionSettingsStore settings,
    IPaymentHistoryReader history,
    IBillingKpiReader billing,
    IPreventionPayablesReader payables,
    TaxDeclarations tax,
    ICompanyProfileRepository profiles,
    IThirdPartyCheckStore checks,
    ITenantContext tenant,
    DossierPeerComparison peers)
{
    private static readonly FiscalObligationDto[] VatObligations =
        [FiscalObligationDto.VatReturnCa3, FiscalObligationDto.VatReturnCa12, FiscalObligationDto.VatInstallment];

    public async Task<PreventionRadarResponse> ComputeAsync(DateOnly today, CancellationToken cancellationToken) =>
        PreventionRules.Compute(await ReadAsync(today, cancellationToken).ConfigureAwait(false), today);

    public async Task<PreventionInput> ReadAsync(DateOnly today, CancellationToken cancellationToken)
    {
        PreventionSettings current = await settings.FindAsync(cancellationToken).ConfigureAwait(false) ?? PreventionSettings.Default();
        CompanyProfile? profile = await profiles.FindAsync(cancellationToken).ConfigureAwait(false);
        (IReadOnlyList<PreventionVatPayment> vat, PreventionUnavailableDto vatUnavailable) =
            await VatAsync(profile, today, current.TreasuryHorizonWeeks * 7, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ThirdPartyEntry> parties = await checks.ListAsync(cancellationToken).ConfigureAwait(false);
        return new PreventionInput(
            current,
            await history.ReadAsync(cancellationToken).ConfigureAwait(false),
            await billing.ReadAsync(cancellationToken).ConfigureAwait(false),
            await payables.ReadAsync(cancellationToken).ConfigureAwait(false),
            vat,
            vatUnavailable,
            await peers.CompareAsync(tenant.Current.DossierId, cancellationToken).ConfigureAwait(false),
            [
                .. parties
                    .Where(p => p.Kind == ThirdPartyKind.Customer)
                    .Select(p => new PreventionCustomerCheck(p.PartyId, p.Name, p.Check is not null, p.Check?.Notice, p.Check?.NoticeOn)),
            ]);
    }

    /// <summary>
    /// Les échéances de TVA à venir, pas encore déposées, dont le montant est connu : CA3 et CA12 d'une période close
    /// (TVA nette du brouillon, moins les acomptes pour la CA12), acomptes de CA12. La TVA d'une période pas encore finie
    /// n'est pas comptée : ses ventes ne sont pas toutes connues.
    /// </summary>
    private async Task<(IReadOnlyList<PreventionVatPayment> Payments, PreventionUnavailableDto Unavailable)> VatAsync(
        CompanyProfile? profile, DateOnly today, int horizonDays, CancellationToken cancellationToken)
    {
        if (profile is null)
        {
            return ([], PreventionUnavailableDto.NoCompanyProfile);
        }

        if (profile.VatFranchise)
        {
            return ([], PreventionUnavailableDto.VatFranchise);
        }

        if (profile.VatDeclarationRegime is null)
        {
            return ([], PreventionUnavailableDto.NoVatRegime);
        }

        int days = Math.Max(PreventionRules.VatLookAheadDays, horizonDays);
        Result<IReadOnlyList<FiscalDeadlineResponse>> upcoming = await tax.UpcomingAsync(today, days, cancellationToken).ConfigureAwait(false);
        if (upcoming.IsFailure)
        {
            return ([], PreventionUnavailableDto.NoCompanyProfile);
        }

        List<PreventionVatPayment> payments = [];
        foreach (FiscalDeadlineResponse deadline in upcoming.Value.Where(d => d.FiledAt is null && d.DueDate >= today && VatObligations.Contains(d.Obligation)))
        {
            if (await AmountAsync(deadline, today, cancellationToken).ConfigureAwait(false) is decimal amount)
            {
                payments.Add(new PreventionVatPayment(deadline.Obligation, deadline.PeriodStart, deadline.PeriodEnd, deadline.DueDate, amount));
            }
        }

        return (payments, PreventionUnavailableDto.None);
    }

    private async Task<decimal?> AmountAsync(FiscalDeadlineResponse deadline, DateOnly today, CancellationToken cancellationToken)
    {
        if (deadline.Obligation == FiscalObligationDto.VatInstallment)
        {
            Result<VatReturnResponse> year = await tax.VatReturnAsync(deadline.DueDate, cancellationToken).ConfigureAwait(false);
            return year.IsSuccess && year.Value.Installments.FirstOrDefault(i => i.DueDate == deadline.DueDate) is { Exempt: false } installment
                ? installment.Amount
                : null;
        }

        if (deadline.PeriodEnd >= today)
        {
            return null;
        }

        Result<VatReturnResponse> draft = await tax.VatReturnAsync(deadline.PeriodStart, cancellationToken).ConfigureAwait(false);
        if (draft.IsFailure)
        {
            return null;
        }

        return deadline.Obligation == FiscalObligationDto.VatReturnCa12
            ? Math.Max(0m, draft.Value.NetVatDue - draft.Value.Installments.Where(i => !i.Exempt).Sum(i => i.Amount))
            : draft.Value.NetVatDue;
    }
}

internal sealed class GetPreventionRadarHandler(PreventionRadarReader reader, ICurrentUser user, IPermissionService permissions, TimeProvider clock)
    : IQueryHandler<GetPreventionRadarQuery, Result<PreventionRadarResponse>>
{
    public async Task<Result<PreventionRadarResponse>> HandleAsync(GetPreventionRadarQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        PreventionRadarResponse radar = await reader.ComputeAsync(LegalCalendar.Today(clock), cancellationToken).ConfigureAwait(false);
        IReadOnlySet<Permission> granted = await permissions
            .GetEffectiveAsync(user.UserId, AuthorizationScope.ForDossier(query.Dossier), cancellationToken)
            .ConfigureAwait(false);
        return radar with { CanAdjust = granted.Contains(Permission.PreventionRegler) };
    }
}

internal sealed class GetPreventionSummaryHandler(PreventionRadarReader reader, ICurrentUser user, IPermissionService permissions, TimeProvider clock)
    : IQueryHandler<GetPreventionSummaryQuery, Result<PreventionSummaryResponse>>
{
    public async Task<Result<PreventionSummaryResponse>> HandleAsync(GetPreventionSummaryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlySet<Permission> granted = await permissions
            .GetEffectiveAsync(user.UserId, AuthorizationScope.ForDossier(query.Dossier), cancellationToken)
            .ConfigureAwait(false);
        if (!granted.Contains(query.Permission))
        {
            return PreventionSummaryResponse.Hidden;
        }

        return PreventionRules.Summarize(await reader.ComputeAsync(LegalCalendar.Today(clock), cancellationToken).ConfigureAwait(false));
    }
}

internal sealed class UpdatePreventionSettingsHandler(IPreventionSettingsStore store, IUnitOfWork unitOfWork, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<UpdatePreventionSettingsCommand, Result<PreventionSettingsResponse>>
{
    public async Task<Result<PreventionSettingsResponse>> HandleAsync(UpdatePreventionSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        UpdatePreventionSettingsRequest request = command.Request ?? new UpdatePreventionSettingsRequest();
        PreventionSettings? stored = await store.FindAsync(cancellationToken).ConfigureAwait(false);
        Result<PreventionSettings> loaded = stored is null
            ? (request.Version == 0 ? PreventionSettings.Default() : ConcurrencyErrors.VersionMismatch)
            : Concurrency.Load(stored, ConcurrencyErrors.VersionMismatch, request.Version);
        if (loaded.IsFailure)
        {
            return Result<PreventionSettingsResponse>.Failure(loaded.Errors);
        }

        DateTimeOffset now = clock.GetUtcNow();
        PreventionThresholds thresholds = new(
            request.TreasuryHorizonWeeks,
            request.CashBalance,
            request.CashBalanceOn,
            request.PaymentDelayRiseDays,
            request.PeerGapPoints,
            request.TopCustomerSharePercent,
            request.BilledCollectedGapPercent,
            request.CallSignalCount,
            [.. (request.Disabled ?? []).Select(s => (PreventionSignal)(int)s)]);
        Result updated = loaded.Value.Update(thresholds, user.UserId, now, LegalCalendar.DateOf(now));
        if (updated.IsFailure)
        {
            return Result<PreventionSettingsResponse>.Failure(updated.Errors);
        }

        if (stored is null)
        {
            await store.AddAsync(loaded.Value, cancellationToken).ConfigureAwait(false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PreventionRules.Describe(loaded.Value);
    }
}

internal sealed class GetPreventionLetterHandler(PreventionRadarReader reader, ICompanyProfileRepository profiles, IEmailSender email, TimeProvider clock)
    : IQueryHandler<GetPreventionLetterQuery, Result<PreventionLetterResponse>>
{
    public async Task<Result<PreventionLetterResponse>> HandleAsync(GetPreventionLetterQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        PreventionRadarResponse radar = await reader.ComputeAsync(LegalCalendar.Today(clock), cancellationToken).ConfigureAwait(false);
        string company = (await profiles.FindAsync(cancellationToken).ConfigureAwait(false))?.Name ?? string.Empty;
        string language = PreventionLetterTemplates.LanguageOf(query.Language);
        PreventionSignalDto[] topics = [.. radar.Signals.Where(s => s.IsActive).Select(s => s.Signal)];
        (string subject, string body) = PreventionLetterTemplates.Write(language, company, topics);
        return new PreventionLetterResponse(language, company, subject, body, topics, email.IsConfigured);
    }
}

internal sealed class SendPreventionLetterHandler(IEmailSender email) : ICommandHandler<SendPreventionLetterCommand, Result>
{
    public async Task<Result> HandleAsync(SendPreventionLetterCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        SendPreventionLetterRequest request = command.Request ?? new SendPreventionLetterRequest();
        List<Error> errors = [];
        Result<Email> to = string.IsNullOrWhiteSpace(request.To) ? PreventionLetterErrors.RecipientRequired : Email.Create(request.To);
        if (to.IsFailure)
        {
            errors.AddRange(to.Errors);
        }

        string subject = (request.Subject ?? string.Empty).Trim();
        if (subject.Length == 0)
        {
            errors.Add(PreventionLetterErrors.SubjectRequired);
        }
        else if (subject.Length > SendPreventionLetterRequest.MaxSubjectLength || subject.Contains('\n', StringComparison.Ordinal))
        {
            errors.Add(PreventionLetterErrors.SubjectTooLong);
        }

        string body = (request.Body ?? string.Empty).Trim();
        if (body.Length == 0)
        {
            errors.Add(PreventionLetterErrors.BodyRequired);
        }
        else if (body.Length > SendPreventionLetterRequest.MaxBodyLength)
        {
            errors.Add(PreventionLetterErrors.BodyTooLong);
        }

        if (errors.Count > 0)
        {
            return Result.Failure(errors);
        }

        return await email.SendAsync(new EmailMessage(to.Value, subject, body), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Le radar dans la vue cabinet : le nombre de signaux allumés et le niveau, rien d'autre. Réservé, à la lecture, à qui a
/// <see cref="Permission.PreventionConsulter"/> sur le dossier ; jamais publié vers Grafana.
/// </summary>
internal sealed class PreventionIndicatorSource(PreventionRadarReader reader) : IDossierIndicatorSource
{
    public async Task<IReadOnlyList<DossierIndicator>> ComputeAsync(DateOnly today, CancellationToken cancellationToken) =>
        [PreventionRules.Indicator(await reader.ComputeAsync(today, cancellationToken).ConfigureAwait(false))];
}
