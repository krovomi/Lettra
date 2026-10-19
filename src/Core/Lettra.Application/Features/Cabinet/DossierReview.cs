using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Cabinet;
using Lettra.Domain.Common;
using Lettra.Domain.Review;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Cabinet;

/// <summary>La revue du dossier pour une période : chaque point de la liste, coché ou non, avec qui et quand.</summary>
public sealed record GetDossierReviewQuery(DossierId Dossier, string Period)
    : IQuery<Result<DossierReviewResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>
/// Cocher ou décocher un point de la revue. Exige de travailler sur le dossier (saisie des factures) : un auditeur ou un
/// client lit la revue, il ne la coche pas.
/// </summary>
public sealed record SetDossierReviewItemCommand(DossierId Dossier, string Period, string ItemKey, bool Checked, string? Note)
    : ICommand<Result<DossierReviewResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureSaisir;
}

internal sealed class DossierReviewHandlers(
    IDossierReviewStore store,
    ICurrentUser user,
    IAuthorizationDirectory directory,
    IAuthorizationRegistry registry,
    TimeProvider clock) :
    IQueryHandler<GetDossierReviewQuery, Result<DossierReviewResponse>>,
    ICommandHandler<SetDossierReviewItemCommand, Result<DossierReviewResponse>>
{
    public async Task<Result<DossierReviewResponse>> HandleAsync(GetDossierReviewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Result<ReviewPeriod> period = ReviewPeriod.Parse(query.Period);
        return period.IsFailure
            ? Result<DossierReviewResponse>.Failure(period.Errors)
            : await ReadAsync(period.Value, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<DossierReviewResponse>> HandleAsync(SetDossierReviewItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<ReviewPeriod> period = ReviewPeriod.Parse(command.Period);
        if (period.IsFailure)
        {
            return Result<DossierReviewResponse>.Failure(period.Errors);
        }

        if (command.Checked)
        {
            Result<DossierReviewMark> mark = DossierReviewMark.Check(period.Value, command.ItemKey, user.UserId, clock.GetUtcNow(), command.Note);
            if (mark.IsFailure)
            {
                return Result<DossierReviewResponse>.Failure(mark.Errors);
            }

            await store.SaveAsync(mark.Value, cancellationToken).ConfigureAwait(false);
        }
        else if (!DossierReviewChecklist.Contains(command.ItemKey))
        {
            return ReviewErrors.ItemUnknown;
        }
        else
        {
            await store.RemoveAsync(period.Value, command.ItemKey, cancellationToken).ConfigureAwait(false);
        }

        return await ReadAsync(period.Value, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<DossierReviewResponse>> ReadAsync(ReviewPeriod period, CancellationToken cancellationToken)
    {
        IReadOnlyList<DossierReviewMark> marks = await store.ListAsync(period, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<Guid, string> names = await NamesAsync(marks, cancellationToken).ConfigureAwait(false);
        Dictionary<string, DossierReviewMark> byItem = marks.ToDictionary(m => m.ItemKey, StringComparer.Ordinal);
        DossierReviewItemDto[] items =
        [
            .. DossierReviewChecklist.Items.Select(key => byItem.TryGetValue(key, out DossierReviewMark? mark)
                ? new DossierReviewItemDto(key, true, mark.CheckedBy, names.GetValueOrDefault(mark.CheckedBy), mark.CheckedBy == user.UserId, mark.CheckedAt, mark.Note)
                : new DossierReviewItemDto(key, false, null, null, false, null, null)),
        ];
        return new DossierReviewResponse(period.ToString(), items.Count(i => i.Checked), items);
    }

    /// <summary>Le nom de qui a coché, lu dans le registre du cabinet ; inconnu sur un hôte sans registre (poste isolé).</summary>
    private async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IReadOnlyList<DossierReviewMark> marks, CancellationToken cancellationToken)
    {
        if (marks.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        UserAuthorizationProfile? profile = await directory.FindUserAsync(user.UserId, cancellationToken).ConfigureAwait(false);
        if (profile?.Cabinet is not OrganisationId cabinet)
        {
            return new Dictionary<Guid, string>();
        }

        CabinetRegistry? read = await registry.ReadAsync(cabinet, cancellationToken).ConfigureAwait(false);
        return (read?.People ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.DisplayName))
            .ToDictionary(p => p.Id, p => p.DisplayName!);
    }
}
