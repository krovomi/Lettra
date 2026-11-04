using Lettra.Domain.Common;
using Lettra.Domain.Prevention;

namespace Lettra.Domain.Tests.Prevention;

/// <summary>Les seuils du radar de prévention : valeurs proposées, bornes, solde daté, signaux coupés, version.</summary>
public sealed class PreventionSettingsTests
{
    private static readonly DateOnly Today = new(2026, 11, 3);
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);

    private static PreventionThresholds Thresholds(
        int horizon = 8, decimal? cash = null, DateOnly? cashOn = null, int rise = 5, int gap = 15, int share = 50, int billed = 20, int call = 3,
        params PreventionSignal[] disabled) =>
        new(horizon, cash, cashOn, rise, gap, share, billed, call, disabled);

    [Fact]
    public void Les_valeurs_proposees_s_appliquent_tant_que_rien_n_est_regle()
    {
        PreventionSettings settings = PreventionSettings.Default();

        settings.Id.Should().Be(PreventionSettings.SingletonId);
        settings.TreasuryHorizonWeeks.Should().Be(8);
        settings.CashBalance.Should().BeNull();
        settings.PaymentDelayRiseDays.Should().Be(5);
        settings.PeerGapPoints.Should().Be(15);
        settings.TopCustomerSharePercent.Should().Be(50);
        settings.BilledCollectedGapPercent.Should().Be(20);
        settings.CallSignalCount.Should().Be(3);
        settings.Disabled.Should().BeEmpty();
        settings.Version.Should().Be(0);
    }

    [Fact]
    public void Un_reglage_valide_remplace_tout_arrondit_le_solde_et_monte_la_version()
    {
        PreventionSettings settings = PreventionSettings.Default();
        Guid user = Guid.CreateVersion7();

        Result updated = settings.Update(
            Thresholds(12, 15_000.456m, Today.AddDays(-2), 10, 20, 60, 30, 4, PreventionSignal.PeerGap, PreventionSignal.Treasury, PreventionSignal.PeerGap), user, Now, Today);

        updated.IsSuccess.Should().BeTrue();
        settings.TreasuryHorizonWeeks.Should().Be(12);
        settings.CashBalance.Should().Be(15_000.46m);
        settings.CashBalanceOn.Should().Be(Today.AddDays(-2));
        settings.PaymentDelayRiseDays.Should().Be(10);
        settings.PeerGapPoints.Should().Be(20);
        settings.TopCustomerSharePercent.Should().Be(60);
        settings.BilledCollectedGapPercent.Should().Be(30);
        settings.CallSignalCount.Should().Be(4);
        settings.DisabledSignals.Should().Be("1,3");
        settings.Disabled.Should().BeEquivalentTo([PreventionSignal.Treasury, PreventionSignal.PeerGap]);
        settings.UpdatedBy.Should().Be(user);
        settings.UpdatedAt.Should().Be(Now);
        settings.Version.Should().Be(1);
    }

    [Fact]
    public void Effacer_le_solde_efface_aussi_sa_date()
    {
        PreventionSettings settings = PreventionSettings.Default();
        settings.Update(Thresholds(cash: 100m, cashOn: Today), Guid.Empty, Now, Today);

        settings.Update(Thresholds(cash: null, cashOn: Today), Guid.Empty, Now, Today).IsSuccess.Should().BeTrue();

        settings.CashBalance.Should().BeNull();
        settings.CashBalanceOn.Should().BeNull();
        settings.Version.Should().Be(2);
    }

    private static readonly Dictionary<string, (PreventionThresholds Thresholds, string Code, string Target)> OutOfRange = new(StringComparer.Ordinal)
    {
        ["horizon-court"] = (Thresholds(horizon: 1), PreventionErrors.HorizonInvalidCode, nameof(PreventionSettings.TreasuryHorizonWeeks)),
        ["horizon-long"] = (Thresholds(horizon: 27), PreventionErrors.HorizonInvalidCode, nameof(PreventionSettings.TreasuryHorizonWeeks)),
        ["solde-enorme"] = (Thresholds(cash: 1_000_000_000m, cashOn: Today), PreventionErrors.CashBalanceInvalidCode, nameof(PreventionSettings.CashBalance)),
        ["solde-sans-date"] = (Thresholds(cash: 100m), PreventionErrors.CashBalanceDateInvalidCode, nameof(PreventionSettings.CashBalanceOn)),
        ["solde-de-demain"] = (Thresholds(cash: 100m, cashOn: Today.AddDays(1)), PreventionErrors.CashBalanceDateInvalidCode, nameof(PreventionSettings.CashBalanceOn)),
        ["hausse-nulle"] = (Thresholds(rise: 0), PreventionErrors.ThresholdInvalidCode, nameof(PreventionThresholds.PaymentDelayRiseDays)),
        ["hausse-trop-forte"] = (Thresholds(rise: 61), PreventionErrors.ThresholdInvalidCode, nameof(PreventionThresholds.PaymentDelayRiseDays)),
        ["ecart-trop-faible"] = (Thresholds(gap: 4), PreventionErrors.ThresholdInvalidCode, nameof(PreventionThresholds.PeerGapPoints)),
        ["part-trop-faible"] = (Thresholds(share: 19), PreventionErrors.ThresholdInvalidCode, nameof(PreventionThresholds.TopCustomerSharePercent)),
        ["facture-trop-fort"] = (Thresholds(billed: 101), PreventionErrors.ThresholdInvalidCode, nameof(PreventionThresholds.BilledCollectedGapPercent)),
        ["appel-un-seul"] = (Thresholds(call: 1), PreventionErrors.ThresholdInvalidCode, nameof(PreventionThresholds.CallSignalCount)),
        ["appel-huit"] = (Thresholds(call: 8), PreventionErrors.ThresholdInvalidCode, nameof(PreventionThresholds.CallSignalCount)),
        ["signal-inconnu"] = (Thresholds(disabled: (PreventionSignal)42), PreventionErrors.SignalUnknownCode, nameof(PreventionSettings.Disabled)),
    };

    [Theory]
    [InlineData("horizon-court")]
    [InlineData("horizon-long")]
    [InlineData("solde-enorme")]
    [InlineData("solde-sans-date")]
    [InlineData("solde-de-demain")]
    [InlineData("hausse-nulle")]
    [InlineData("hausse-trop-forte")]
    [InlineData("ecart-trop-faible")]
    [InlineData("part-trop-faible")]
    [InlineData("facture-trop-fort")]
    [InlineData("appel-un-seul")]
    [InlineData("appel-huit")]
    [InlineData("signal-inconnu")]
    public void Une_valeur_hors_bornes_est_refusee_sans_rien_changer(string cas)
    {
        (PreventionThresholds thresholds, string code, string target) = OutOfRange[cas];
        PreventionSettings settings = PreventionSettings.Default();

        Result updated = settings.Update(thresholds, Guid.Empty, Now, Today);

        updated.IsFailure.Should().BeTrue();
        updated.Errors.Should().ContainSingle().Which.Should().Match<Error>(e => e.Code == code && e.Target == target);
        settings.Version.Should().Be(0);
        settings.TreasuryHorizonWeeks.Should().Be(PreventionSettings.DefaultHorizonWeeks);
    }

    [Fact]
    public void Un_negatif_est_un_solde_permis_un_decouvert()
    {
        PreventionSettings settings = PreventionSettings.Default();

        settings.Update(Thresholds(cash: -2500m, cashOn: Today), Guid.Empty, Now, Today).IsSuccess.Should().BeTrue();

        settings.CashBalance.Should().Be(-2500m);
    }
}
