using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Features.Cabinet;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Tests;

/// <summary>Une ligne de la vue cabinet : les clés réservées n'y figurent, et ne pèsent sur l'urgence, que pour qui a le droit.</summary>
public sealed class CabinetOverviewMapperTests
{
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());

    private static readonly StoredDossierIndicators Stored = new(
        Dossier,
        DateTimeOffset.UnixEpoch,
        [
            new DossierIndicator(DossierIndicatorKeys.Prevention, 3, IndicatorLevel.Critical, new DateOnly(2026, 12, 1)),
            new DossierIndicator(DossierIndicatorKeys.OverdueInvoices, 1, IndicatorLevel.Warning),
        ]);

    [Fact]
    public void Sans_le_droit_le_radar_de_prevention_disparait_de_la_ligne_et_de_son_urgence()
    {
        DossierOverviewRow row = CabinetOverviewMapper.ToRow(new DossierSummary(Dossier.Value, "Atelier Durand"), Stored, restrictedVisible: false);

        row.Indicators.Select(i => i.Key).Should().Equal(DossierIndicatorKeys.OverdueInvoices);
        row.Level.Should().Be(IndicatorLevelDto.Warning);
    }

    [Fact]
    public void Avec_le_droit_le_radar_prend_sa_place_parmi_les_indicateurs()
    {
        DossierOverviewRow row = CabinetOverviewMapper.ToRow(new DossierSummary(Dossier.Value, "Atelier Durand"), Stored, restrictedVisible: true);

        row.Indicators.Select(i => i.Key).Should().Equal(DossierIndicatorKeys.OverdueInvoices, DossierIndicatorKeys.Prevention);
        row.Level.Should().Be(IndicatorLevelDto.Critical);
        DossierIndicatorKeys.Known.Should().Contain(DossierIndicatorKeys.Prevention);
        DossierIndicatorKeys.Restricted.Should().Equal(DossierIndicatorKeys.Prevention);
    }
}
