using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Dossiers;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Cabinet;

/// <summary>
/// La vue cabinet : une ligne par dossier que l'utilisateur peut ouvrir, avec les indicateurs rangés par la passe quotidienne (ou au
/// dernier « Actualiser »). Une seule lecture du registre des indicateurs : aucune base de dossier n'est ouverte.
/// </summary>
public sealed record GetCabinetOverviewQuery : IQuery<Result<CabinetOverviewResponse>>, IFilterByPermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>
/// Recalcule maintenant les indicateurs des dossiers que l'utilisateur peut ouvrir (tous, ou ceux de
/// <paramref name="Dossiers"/> parmi eux). Ouvre chaque base : c'est le bouton « Actualiser », pas la page.
/// </summary>
public sealed record RefreshCabinetOverviewCommand(IReadOnlyCollection<Guid>? Dossiers = null)
    : ICommand<Result<CabinetOverviewRefreshResponse>>, IFilterByPermission
{
    public Permission Permission => Permission.DossierLire;
}

internal sealed class CabinetOverviewHandlers(AccessibleDossiers accessible, IDossierIndicatorStore store, IDossierIndicatorRefresher refresher) :
    IQueryHandler<GetCabinetOverviewQuery, Result<CabinetOverviewResponse>>,
    ICommandHandler<RefreshCabinetOverviewCommand, Result<CabinetOverviewRefreshResponse>>
{
    public async Task<Result<CabinetOverviewResponse>> HandleAsync(GetCabinetOverviewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyList<DossierSummary> dossiers = await accessible.ListAsync(query.Permission, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<StoredDossierIndicators> stored = await store
            .ReadAsync([.. dossiers.Select(d => new DossierId(d.Id))], cancellationToken).ConfigureAwait(false);
        Dictionary<Guid, StoredDossierIndicators> byDossier = stored.ToDictionary(s => s.Dossier.Value);

        DossierOverviewRow[] rows =
        [
            .. dossiers
                .Select(d => CabinetOverviewMapper.ToRow(d, byDossier.GetValueOrDefault(d.Id)))
                .OrderByDescending(r => r.Level)
                .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
        ];
        return new CabinetOverviewResponse(rows);
    }

    public async Task<Result<CabinetOverviewRefreshResponse>> HandleAsync(RefreshCabinetOverviewCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        IReadOnlyList<DossierSummary> dossiers = await accessible.ListAsync(command.Permission, cancellationToken).ConfigureAwait(false);
        DossierId[] targets =
        [
            .. dossiers
                .Where(d => command.Dossiers is null || command.Dossiers.Contains(d.Id))
                .Select(d => new DossierId(d.Id)),
        ];
        IndicatorRefreshOutcome outcome = await refresher.RefreshAsync(targets, cancellationToken).ConfigureAwait(false);
        return new CabinetOverviewRefreshResponse(outcome.Refreshed, outcome.Failed);
    }
}

internal static class CabinetOverviewMapper
{
    public static DossierOverviewRow ToRow(DossierSummary dossier, StoredDossierIndicators? stored)
    {
        DossierIndicatorDto[] indicators =
        [
            .. (stored?.Indicators ?? [])
                .Select(i => new DossierIndicatorDto(i.Key, i.Count, (IndicatorLevelDto)i.Level, i.DueOn))
                .OrderBy(i => Order(i.Key))
                .ThenBy(i => i.Key, StringComparer.Ordinal),
        ];
        IndicatorLevelDto level = indicators.Length == 0 ? IndicatorLevelDto.Ok : indicators.Max(i => i.Level);
        return new DossierOverviewRow(dossier.Id, dossier.Name, stored?.ComputedAt, level, indicators);
    }

    private static int Order(string key)
    {
        int index = DossierIndicatorKeys.Known.ToList().IndexOf(key);
        return index < 0 ? int.MaxValue : index;
    }
}
