using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Missions;
using Lettra.Domain.Common;
using Lettra.Domain.Missions;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Cabinet;

/// <summary>La mission du dossier, l'année de mission en cours, le temps passé et ce qu'il rapporte.</summary>
public sealed record GetMissionOverviewQuery(DossierId Dossier)
    : IQuery<Result<MissionOverviewResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>
/// Créer ou modifier la mission. Ce qui engage le cabinet (nature, forfait) est l'affaire de qui valide les factures
/// (manager, collaborateur étendu) : un collaborateur saisit son temps, il ne change pas les honoraires.
/// </summary>
public sealed record SaveMissionCommand(DossierId Dossier, Mission.Draft Data, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IVersionedCommand
{
    public Permission Permission => Permission.FactureValider;
}

/// <summary>Saisir un temps passé sur le dossier : tout collaborateur qui y travaille.</summary>
public sealed record AddTimeEntryCommand(DossierId Dossier, DateOnly Date, int Minutes, TimeTask Task, string? Note)
    : ICommand<Result<MissionOverviewResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureSaisir;
}

/// <summary>Supprimer un de ses temps passés (une erreur de saisie) ; on ne supprime pas le temps d'un autre.</summary>
public sealed record DeleteTimeEntryCommand(DossierId Dossier, Guid Id)
    : ICommand<Result<MissionOverviewResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureSaisir;
}

internal sealed class MissionHandlers(
    IMissionStore store,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    CabinetPeopleReader people,
    TimeProvider clock) :
    IQueryHandler<GetMissionOverviewQuery, Result<MissionOverviewResponse>>,
    ICommandHandler<SaveMissionCommand, Result>,
    ICommandHandler<AddTimeEntryCommand, Result<MissionOverviewResponse>>,
    ICommandHandler<DeleteTimeEntryCommand, Result<MissionOverviewResponse>>
{
    public async Task<Result<MissionOverviewResponse>> HandleAsync(GetMissionOverviewQuery query, CancellationToken cancellationToken) =>
        await OverviewAsync(cancellationToken).ConfigureAwait(false);

    public async Task<Result> HandleAsync(SaveMissionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Mission? mission = await store.FindAsync(cancellationToken).ConfigureAwait(false);
        if (mission is null)
        {
            Result<Mission> created = Mission.Create(command.Data, clock.GetUtcNow());
            if (created.IsFailure)
            {
                return Result.Failure(created.Errors);
            }

            await store.AddAsync(created.Value, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            Result updated = Concurrency.Check(mission, command.ExpectedVersion);
            if (updated.IsSuccess)
            {
                updated = mission.Update(command.Data, clock.GetUtcNow());
            }

            if (updated.IsFailure)
            {
                return updated;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result<MissionOverviewResponse>> HandleAsync(AddTimeEntryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        DateTimeOffset now = clock.GetUtcNow();
        Result<TimeEntry> entry = TimeEntry.Create(Guid.CreateVersion7(), command.Date, user.UserId, command.Minutes, command.Task, command.Note, LegalCalendar.DateOf(now), now);
        if (entry.IsFailure)
        {
            return Result<MissionOverviewResponse>.Failure(entry.Errors);
        }

        await store.AddTimeAsync(entry.Value, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await OverviewAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<MissionOverviewResponse>> HandleAsync(DeleteTimeEntryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        TimeEntry? entry = await store.FindTimeAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return MissionErrors.TimeEntryNotFound;
        }

        if (entry.UserId != user.UserId)
        {
            return MissionErrors.TimeEntryNotYours;
        }

        await store.RemoveTimeAsync(entry, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await OverviewAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<MissionOverviewResponse>> OverviewAsync(CancellationToken cancellationToken)
    {
        DateOnly today = LegalCalendar.DateOf(clock.GetUtcNow());
        Mission? mission = await store.FindAsync(cancellationToken).ConfigureAwait(false);
        (DateOnly from, DateOnly to) = MissionOverview.Year(mission, today);
        IReadOnlyList<TimeEntry> entries = await store.ListTimeAsync(from, to, cancellationToken).ConfigureAwait(false);
        CabinetPeople cabinet = await people.ReadAsync(cancellationToken).ConfigureAwait(false);
        return MissionOverview.Build(mission, today, entries, cabinet, user.UserId);
    }
}

/// <summary>Les calculs de l'écran « Mission », en fonctions pures.</summary>
public static class MissionOverview
{
    /// <summary>L'année de mission qui contient <paramref name="today"/> ; sans mission, les douze derniers mois.</summary>
    public static (DateOnly From, DateOnly To) Year(Mission? mission, DateOnly today) =>
        mission?.YearOf(today) ?? (today.AddYears(-1).AddDays(1), today);

    /// <summary>
    /// Les honoraires acquis à <paramref name="today"/> : le forfait au prorata des jours écoulés de l'année de mission
    /// (rien avant son début, tout après sa fin).
    /// </summary>
    public static decimal Earned(Mission mission, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(mission);
        (DateOnly from, DateOnly to) = mission.YearOf(today);
        int days = to.DayNumber - from.DayNumber + 1;
        int elapsed = Math.Clamp(today.DayNumber - from.DayNumber + 1, 0, days);
        return Math.Round(mission.AnnualFeeExclTax * elapsed / days, 2, MidpointRounding.AwayFromZero);
    }

    public static MissionOverviewResponse Build(Mission? mission, DateOnly today, IReadOnlyList<TimeEntry> entries, CabinetPeople cabinet, Guid currentUser)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(cabinet);
        (DateOnly from, DateOnly to) = Year(mission, today);
        int minutes = entries.Sum(e => e.Minutes);
        decimal earned = mission is null ? 0m : Earned(mission, today);
        decimal? rate = mission is not null && minutes > 0 ? Math.Round(earned * 60m / minutes, 2, MidpointRounding.AwayFromZero) : null;
        return new MissionOverviewResponse(
            mission?.ToResponse(),
            cabinet.CabinetName,
            from,
            to,
            minutes,
            earned,
            rate,
            [.. entries.GroupBy(e => e.Task).OrderBy(g => g.Key).Select(g => new TimeTaskTotal((TimeTaskDto)(int)g.Key, g.Sum(e => e.Minutes)))],
            [
                .. entries.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt).Select(e => new TimeEntryResponse(
                    e.Id, e.Date, e.UserId, cabinet.Names.GetValueOrDefault(e.UserId), e.Minutes, (TimeTaskDto)(int)e.Task, e.Note, e.UserId == currentUser)),
            ]);
    }

    public static MissionResponse ToResponse(this Mission mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        return new MissionResponse(
            (MissionKindDto)(int)mission.Kind,
            [.. Enum.GetValues<MissionServiceDto>().Where(s => mission.Services.HasFlag((MissionServices)(int)s))],
            mission.StartDate,
            mission.AnnualFeeExclTax,
            (FeeScheduleDto)(int)mission.Schedule,
            mission.FeePerInstalment,
            mission.TargetHourlyRate,
            mission.BudgetHours,
            mission.ClientSignatory,
            mission.Manager,
            mission.SignedOn,
            mission.Version);
    }

    public static Mission.Draft ToDraft(this SaveMissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new Mission.Draft(
            (MissionKind)(int)request.Kind,
            (MissionServices)request.Services.Aggregate(0, (all, s) => all | (int)s),
            request.StartDate,
            request.AnnualFeeExclTax,
            (FeeSchedule)(int)request.Schedule,
            request.TargetHourlyRate,
            request.ClientSignatory,
            request.Manager,
            request.SignedOn);
    }
}
