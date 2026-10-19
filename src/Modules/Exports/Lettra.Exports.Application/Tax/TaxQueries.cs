using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Tax;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Exports.Application.Tax;

/// <summary>Brouillon de la déclaration de TVA (CA3 ou CA12 selon le régime) de la période qui contient <paramref name="Date"/>.</summary>
public sealed record GetVatReturnQuery(DossierId Dossier, DateOnly Date) : IQuery<Result<VatReturnResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Déclaration européenne de services d'un mois.</summary>
public sealed record GetDesQuery(DossierId Dossier, int Year, int Month) : IQuery<Result<DesResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Fichier CSV de la DES d'un mois (même permission que le FEC : une donnée qui sort du dossier).</summary>
public sealed record ExportDesQuery(DossierId Dossier, int Year, int Month) : IQuery<Result<DesFile>>, IRequirePermission
{
    public Permission Permission => Permission.FecExporter;
}

/// <summary>Un fichier de DES prêt à déposer soi-même.</summary>
public sealed record DesFile(string FileName, byte[] Content);

/// <summary>Calendrier fiscal du dossier sur une période (24 mois au plus).</summary>
public sealed record GetFiscalCalendarQuery(DossierId Dossier, DateOnly From, DateOnly To) : IQuery<Result<FiscalCalendarResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Échéances en retard ou dues d'ici <paramref name="Days"/> jours (vue cabinet : « TVA à déclarer dans les 7 jours »).</summary>
public sealed record GetUpcomingFiscalDeadlinesQuery(DossierId Dossier, int Days = FiscalCalendar.DueSoonDays)
    : IQuery<Result<IReadOnlyList<FiscalDeadlineResponse>>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

public sealed record GetFiscalSettingsQuery(DossierId Dossier) : IQuery<Result<FiscalSettingsResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Enregistrer les réglages fiscaux (même permission que les exports et déclarations).</summary>
public sealed record UpdateFiscalSettingsCommand(DossierId Dossier, UpdateFiscalSettingsRequest Request) : ICommand<Result<FiscalSettingsResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FecExporter;
}

/// <summary>Marquer une échéance du calendrier comme déposée, ou retirer la marque.</summary>
public sealed record MarkFiscalFilingCommand(DossierId Dossier, MarkFiscalFilingRequest Request) : ICommand<Result<FiscalDeadlineResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FecExporter;
}

internal sealed class GetVatReturnHandler(TaxDeclarations tax) : IQueryHandler<GetVatReturnQuery, Result<VatReturnResponse>>
{
    public Task<Result<VatReturnResponse>> HandleAsync(GetVatReturnQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return tax.VatReturnAsync(query.Date, cancellationToken);
    }
}

internal sealed class GetDesHandler(TaxDeclarations tax) : IQueryHandler<GetDesQuery, Result<DesResponse>>
{
    public Task<Result<DesResponse>> HandleAsync(GetDesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return tax.DesAsync(query.Year, query.Month, cancellationToken);
    }
}

internal sealed class ExportDesHandler(TaxDeclarations tax) : IQueryHandler<ExportDesQuery, Result<DesFile>>
{
    public async Task<Result<DesFile>> HandleAsync(ExportDesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Result<DesResponse> des = await tax.DesAsync(query.Year, query.Month, cancellationToken).ConfigureAwait(false);
        return des.IsFailure
            ? Result<DesFile>.Failure(des.Errors)
            : new DesFile($"DES-{query.Year:D4}-{query.Month:D2}.csv", DesCalculator.ToCsv(des.Value));
    }
}

internal sealed class GetFiscalCalendarHandler(TaxDeclarations tax, TimeProvider clock) : IQueryHandler<GetFiscalCalendarQuery, Result<FiscalCalendarResponse>>
{
    public Task<Result<FiscalCalendarResponse>> HandleAsync(GetFiscalCalendarQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return tax.CalendarAsync(query.From, query.To, LegalCalendar.Today(clock), cancellationToken);
    }
}

internal sealed class GetUpcomingFiscalDeadlinesHandler(TaxDeclarations tax, TimeProvider clock)
    : IQueryHandler<GetUpcomingFiscalDeadlinesQuery, Result<IReadOnlyList<FiscalDeadlineResponse>>>
{
    public Task<Result<IReadOnlyList<FiscalDeadlineResponse>>> HandleAsync(GetUpcomingFiscalDeadlinesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return tax.UpcomingAsync(LegalCalendar.Today(clock), query.Days, cancellationToken);
    }
}

internal sealed class GetFiscalSettingsHandler(TaxDeclarations tax) : IQueryHandler<GetFiscalSettingsQuery, Result<FiscalSettingsResponse>>
{
    public async Task<Result<FiscalSettingsResponse>> HandleAsync(GetFiscalSettingsQuery query, CancellationToken cancellationToken) =>
        (await tax.SettingsAsync(cancellationToken).ConfigureAwait(false)).ToResponse();
}

internal sealed class UpdateFiscalSettingsHandler(IFiscalStore store, TimeProvider clock)
    : ICommandHandler<UpdateFiscalSettingsCommand, Result<FiscalSettingsResponse>>
{
    public async Task<Result<FiscalSettingsResponse>> HandleAsync(UpdateFiscalSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        UpdateFiscalSettingsRequest request = command.Request;
        List<Error> errors = [];
        if (request.FiscalYearEndMonth is < 1 or > 12 || request.FiscalYearEndDay < 1
            || request.FiscalYearEndDay > DateTime.DaysInMonth(2001, Math.Clamp(request.FiscalYearEndMonth, 1, 12)))
        {
            errors.Add(TaxErrors.FiscalYearEndInvalid);
        }

        if (request.VatDeadlineDay is < FiscalSettings.MinDeadlineDay or > FiscalSettings.MaxDeadlineDay)
        {
            errors.Add(TaxErrors.DeadlineDayInvalid);
        }

        if (!Enum.IsDefined(request.VatPeriodicity))
        {
            errors.Add(Error.Validation(Contracts.Common.ErrorCodes.Validation.Invalid, nameof(request.VatPeriodicity)));
        }

        if (errors.Count > 0)
        {
            return Result<FiscalSettingsResponse>.Failure(errors);
        }

        FiscalSettings current = await store.FindSettingsAsync(cancellationToken).ConfigureAwait(false) ?? FiscalSettings.Default;
        if (current.Version != request.Version)
        {
            return TaxErrors.VersionMismatch;
        }

        DateTimeOffset now = clock.GetUtcNow();
        FiscalSettings saved = new(
            request.FiscalYearEndMonth,
            request.FiscalYearEndDay,
            request.VatPeriodicity,
            request.VatDeadlineDay,
            request.CorporateIncomeTax,
            request.Cfe,
            current.TrackedFrom ?? LegalCalendar.Today(clock),
            current.Version + 1);
        await store.SaveSettingsAsync(saved, now, cancellationToken).ConfigureAwait(false);
        return saved.ToResponse();
    }
}

internal sealed class MarkFiscalFilingHandler(TaxDeclarations tax, IFiscalStore store, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<MarkFiscalFilingCommand, Result<FiscalDeadlineResponse>>
{
    public async Task<Result<FiscalDeadlineResponse>> HandleAsync(MarkFiscalFilingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        MarkFiscalFilingRequest request = command.Request;

        // On ne marque qu'une échéance que le calendrier du dossier connaît (fenêtre de trois ans autour de la période).
        DateOnly today = LegalCalendar.Today(clock);
        Result<FiscalCalendarResponse> calendar = await tax.CalendarAsync(request.PeriodStart, request.PeriodStart.AddMonths(24), today, cancellationToken).ConfigureAwait(false);
        if (calendar.IsFailure)
        {
            return Result<FiscalDeadlineResponse>.Failure(calendar.Errors);
        }

        FiscalDeadlineResponse? deadline = calendar.Value.Deadlines.FirstOrDefault(d => d.Obligation == request.Obligation && d.PeriodStart == request.PeriodStart);
        if (deadline is null)
        {
            return TaxErrors.DeadlineUnknown;
        }

        DateTimeOffset now = clock.GetUtcNow();
        FiscalFiling? filing = request.Filed ? new FiscalFiling(request.Obligation, request.PeriodStart, now, user.UserId) : null;
        await store.SetFilingAsync(request.Obligation, request.PeriodStart, filing, cancellationToken).ConfigureAwait(false);

        // L'état se relit dans le calendrier : retirer la marque peut redonner « en retard » comme « à venir ».
        Result<FiscalCalendarResponse> after = await tax.CalendarAsync(request.PeriodStart, request.PeriodStart.AddMonths(24), today, cancellationToken).ConfigureAwait(false);
        return after.IsSuccess && after.Value.Deadlines.FirstOrDefault(d => d.Obligation == request.Obligation && d.PeriodStart == request.PeriodStart) is { } updated
            ? updated
            : deadline;
    }
}
