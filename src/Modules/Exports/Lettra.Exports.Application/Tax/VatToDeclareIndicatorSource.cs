using Lettra.Application.Abstractions.Cabinet;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Tax;
using Lettra.Domain.Common;

namespace Lettra.Exports.Application.Tax;

/// <summary>
/// « TVA à déclarer » pour la vue cabinet : CA3, CA12 et acomptes de TVA en retard ou dus dans les 7 jours, pas encore
/// marqués déposés. Échéance la plus proche ; critique dès qu'une est en retard. Sans fiche entreprise, pas d'indicateur
/// (le dossier n'a pas de calendrier) ; en franchise, zéro.
/// </summary>
public sealed class VatToDeclareIndicatorSource(TaxDeclarations tax) : IDossierIndicatorSource
{
    private static readonly FiscalObligationDto[] VatObligations =
        [FiscalObligationDto.VatReturnCa3, FiscalObligationDto.VatReturnCa12, FiscalObligationDto.VatInstallment];

    public async Task<IReadOnlyList<DossierIndicator>> ComputeAsync(DateOnly today, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<FiscalDeadlineResponse>> upcoming = await tax.UpcomingAsync(today, FiscalCalendar.DueSoonDays, cancellationToken).ConfigureAwait(false);
        return upcoming.IsFailure ? [] : [Indicator(upcoming.Value)];
    }

    public static DossierIndicator Indicator(IEnumerable<FiscalDeadlineResponse> deadlines)
    {
        FiscalDeadlineResponse[] due =
        [
            .. deadlines.Where(d => VatObligations.Contains(d.Obligation) && d.State is FiscalDeadlineStateDto.Overdue or FiscalDeadlineStateDto.DueSoon),
        ];
        if (due.Length == 0)
        {
            return new DossierIndicator(DossierIndicatorKeys.VatToDeclare, 0, IndicatorLevel.Ok);
        }

        IndicatorLevel level = due.Any(d => d.State == FiscalDeadlineStateDto.Overdue) ? IndicatorLevel.Critical : IndicatorLevel.Warning;
        return new DossierIndicator(DossierIndicatorKeys.VatToDeclare, due.Length, level, due.Min(d => d.DueDate));
    }
}
