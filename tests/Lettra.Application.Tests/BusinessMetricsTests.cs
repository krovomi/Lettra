using Lettra.Application.Diagnostics;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Domain.Common;

namespace Lettra.Application.Tests;

/// <summary>Statut des transmissions à la plateforme dans les métriques métier : un nom fixe, jamais une valeur saisie.</summary>
public sealed class BusinessMetricsTests
{
    [Theory]
    [InlineData(PlatformStatusDto.Deposited, "deposited")]
    [InlineData(PlatformStatusDto.MadeAvailable, "made_available")]
    public void Le_statut_rendu_par_la_plateforme_devient_un_nom_fixe(PlatformStatusDto status, string expected) =>
        BusinessMetrics.StatusName(status).Should().Be(expected);

    [Fact]
    public void Un_envoi_mis_en_file_est_compte_en_attente_et_un_refus_comme_refuse()
    {
        BusinessMetrics.PlatformStatusOf(LettraTelemetry.Outcomes.Failure, null, [Error.Conflict(ErrorCodes.Platform.Queued)])
            .Should().Be("queued");
        BusinessMetrics.PlatformStatusOf(LettraTelemetry.Outcomes.Failure, null, [Error.Conflict(ErrorCodes.Platform.NotTransmitted)])
            .Should().Be("refused");
        BusinessMetrics.PlatformStatusOf(LettraTelemetry.Outcomes.Error, null, [])
            .Should().Be("error");
    }
}
