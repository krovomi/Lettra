using Lettra.Application.Abstractions.Cabinet;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Tax;
using Lettra.Exports.Application.Tax;

namespace Lettra.Exports.Tests.Tax;

/// <summary>Indicateur « TVA à déclarer » de la vue cabinet : ce qui compte, l'échéance retenue, l'urgence.</summary>
public sealed class VatToDeclareIndicatorTests
{
    private static FiscalDeadlineResponse Deadline(FiscalObligationDto obligation, int month, int day, FiscalDeadlineStateDto state) =>
        new(obligation, new DateOnly(2026, month, 1), new DateOnly(2026, month, 28), new DateOnly(2026, month, day), state, null);

    [Fact]
    public void Sans_tva_due_l_indicateur_est_a_zero()
    {
        DossierIndicator indicator = VatToDeclareIndicatorSource.Indicator(
        [
            Deadline(FiscalObligationDto.VatReturnCa3, 11, 15, FiscalDeadlineStateDto.Upcoming),
            Deadline(FiscalObligationDto.VatReturnCa3, 9, 15, FiscalDeadlineStateDto.Filed),
        ]);

        indicator.Should().Be(new DossierIndicator(DossierIndicatorKeys.VatToDeclare, 0, IndicatorLevel.Ok));
    }

    [Fact]
    public void Une_tva_proche_avertit_avec_son_echeance_et_la_des_ou_l_is_ne_comptent_pas()
    {
        DossierIndicator indicator = VatToDeclareIndicatorSource.Indicator(
        [
            Deadline(FiscalObligationDto.VatReturnCa3, 10, 19, FiscalDeadlineStateDto.DueSoon),
            Deadline(FiscalObligationDto.Des, 10, 14, FiscalDeadlineStateDto.Overdue),
            Deadline(FiscalObligationDto.CorporateTaxInstallment, 10, 15, FiscalDeadlineStateDto.DueSoon),
        ]);

        indicator.Should().Be(new DossierIndicator(DossierIndicatorKeys.VatToDeclare, 1, IndicatorLevel.Warning, new DateOnly(2026, 10, 19)));
    }

    [Fact]
    public void Une_tva_en_retard_rend_l_indicateur_critique_et_donne_la_plus_ancienne_echeance()
    {
        DossierIndicator indicator = VatToDeclareIndicatorSource.Indicator(
        [
            Deadline(FiscalObligationDto.VatReturnCa3, 10, 19, FiscalDeadlineStateDto.DueSoon),
            Deadline(FiscalObligationDto.VatInstallment, 7, 15, FiscalDeadlineStateDto.Overdue),
        ]);

        indicator.Should().Be(new DossierIndicator(DossierIndicatorKeys.VatToDeclare, 2, IndicatorLevel.Critical, new DateOnly(2026, 7, 15)));
    }
}
