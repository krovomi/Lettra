using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Vigilance;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Domain.Vigilance;

namespace Lettra.Application.Features.Cabinet;

/// <summary>La fiche anti-blanchiment du client et où elle en est aujourd'hui.</summary>
public sealed record GetVigilanceQuery(DossierId Dossier) : IQuery<Result<VigilanceOverviewResponse>>, IRequirePermission
{
    public Permission Permission => Permission.VigilanceConsulter;
}

/// <summary>
/// Remplir ou revoir la fiche : chaque enregistrement vaut revue, signée par l'utilisateur. Le niveau de risque engage
/// le cabinet : c'est l'affaire du manager ou du collaborateur étendu.
/// </summary>
public sealed record SaveVigilanceCommand(DossierId Dossier, ClientDueDiligence.Draft Data, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IVersionedCommand
{
    public Permission Permission => Permission.VigilanceGerer;
}

internal sealed class VigilanceHandlers(
    IVigilanceStore store,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    CabinetPeopleReader people,
    TimeProvider clock) :
    IQueryHandler<GetVigilanceQuery, Result<VigilanceOverviewResponse>>,
    ICommandHandler<SaveVigilanceCommand, Result>
{
    public async Task<Result<VigilanceOverviewResponse>> HandleAsync(GetVigilanceQuery query, CancellationToken cancellationToken)
    {
        ClientDueDiligence? file = await store.FindAsync(cancellationToken).ConfigureAwait(false);
        CabinetPeople cabinet = file is null ? CabinetPeople.None : await people.ReadAsync(cancellationToken).ConfigureAwait(false);
        return DueDiligenceOverview.Build(file, LegalCalendar.DateOf(clock.GetUtcNow()), cabinet);
    }

    public async Task<Result> HandleAsync(SaveVigilanceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        DateTimeOffset now = clock.GetUtcNow();
        DateOnly today = LegalCalendar.DateOf(now);
        ClientDueDiligence? file = await store.FindAsync(cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            Result<ClientDueDiligence> created = ClientDueDiligence.Create(command.Data, user.UserId, today, now);
            if (created.IsFailure)
            {
                return Result.Failure(created.Errors);
            }

            await store.AddAsync(created.Value, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            Result updated = Concurrency.Check(file, command.ExpectedVersion);
            if (updated.IsSuccess)
            {
                updated = file.Update(command.Data, user.UserId, today, now);
            }

            if (updated.IsFailure)
            {
                return updated;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>Les calculs de l'écran « Anti-blanchiment » et de son indicateur, en fonctions pures.</summary>
public static class DueDiligenceOverview
{
    /// <summary>Une revue qui tombe dans ce délai s'annonce dans la vue cabinet.</summary>
    public const int ReviewNoticeDays = 30;

    public static DueDiligenceStatusDto StatusOf(ClientDueDiligence? file, DateOnly today) =>
        file is null ? DueDiligenceStatusDto.Missing : (DueDiligenceStatusDto)(int)file.StatusOn(today);

    public static VigilanceOverviewResponse Build(ClientDueDiligence? file, DateOnly today, CabinetPeople cabinet)
    {
        ArgumentNullException.ThrowIfNull(cabinet);
        return new VigilanceOverviewResponse(file?.ToResponse(cabinet.Names.GetValueOrDefault(file.ReviewedBy)), StatusOf(file, today));
    }

    /// <summary>
    /// L'indicateur de la vue cabinet : fiche absente ou incomplète, revue dépassée (critique pour un risque élevé), ou
    /// revue dans les trente jours. Rien pour une fiche à jour ni pour une relation terminée. Ni nom ni niveau de risque.
    /// </summary>
    public static DossierIndicator? Indicator(ClientDueDiligence? file, DateOnly today) => StatusOf(file, today) switch
    {
        DueDiligenceStatusDto.Missing or DueDiligenceStatusDto.Incomplete => new DossierIndicator(DossierIndicatorKeys.ClientDueDiligence, 1, IndicatorLevel.Warning),
        DueDiligenceStatusDto.ReviewDue => new DossierIndicator(
            DossierIndicatorKeys.ClientDueDiligence, 1, file!.Level == RiskLevel.High ? IndicatorLevel.Critical : IndicatorLevel.Warning, file.NextReviewOn),
        DueDiligenceStatusDto.UpToDate when file!.NextReviewOn.DayNumber - today.DayNumber <= ReviewNoticeDays =>
            new DossierIndicator(DossierIndicatorKeys.ClientDueDiligence, 1, IndicatorLevel.Info, file.NextReviewOn),
        _ => null,
    };

    public static DueDiligenceResponse ToResponse(this ClientDueDiligence file, string? reviewedByName)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new DueDiligenceResponse(
            [.. Enum.GetValues<IdentityEvidenceDto>().Where(e => file.Evidence.HasFlag((IdentityEvidence)(int)e))],
            file.IdentityVerifiedOn,
            [.. file.Owners.Select(o => new BeneficialOwnerDto { Name = o.Name, SharePercent = o.SharePercent, Kind = (OwnershipKindDto)(int)o.Kind })],
            (RiskLevelDto)(int)file.Level,
            [.. Enum.GetValues<RiskFactorDto>().Where(f => file.Factors.HasFlag((RiskFactors)(int)f))],
            file.Justification,
            file.ReviewedOn,
            file.ReviewedBy,
            reviewedByName,
            file.NextReviewOn,
            file.RelationshipEndedOn,
            file.KeepUntil,
            file.Version);
    }

    public static ClientDueDiligence.Draft ToDraft(this SaveDueDiligenceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new ClientDueDiligence.Draft(
            (IdentityEvidence)request.Evidence.Aggregate(0, (all, e) => all | (int)e),
            request.IdentityVerifiedOn,
            [.. request.Owners.Select(o => new ClientDueDiligence.OwnerDraft(o.Name ?? string.Empty, o.SharePercent, (OwnershipKind)(int)o.Kind))],
            (RiskLevel)(int)request.Level,
            (RiskFactors)request.Factors.Aggregate(0, (all, f) => all | (int)f),
            request.Justification,
            request.ReviewedOn,
            request.RelationshipEndedOn);
    }
}
