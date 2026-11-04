using Lettra.Contracts.Insights;
using Lettra.Domain.Prevention;
using Lettra.Exports.Application.Insights.Prevention;

namespace Lettra.Exports.Tests.Insights;

/// <summary>
/// Le courrier type de proposition de rendez-vous : dans la langue demandée, il cite les sujets en termes neutres, sans
/// jamais dire le niveau d'attention, le mot « difficulté » ni un chiffre.
/// </summary>
public sealed class PreventionLetterTests
{
    [Fact]
    public void Le_courrier_francais_propose_un_rendez_vous_et_liste_les_sujets_sans_jugement()
    {
        (string subject, string body) = PreventionLetterTemplates.Write(
            PreventionLetterTemplates.French, "Atelier Durand", [PreventionSignalDto.Treasury, PreventionSignalDto.TopCustomer, PreventionSignalDto.Treasury]);

        subject.Should().Be("Proposition de rendez-vous : faire le point sur les prochains mois");
        body.Should().StartWith("Bonjour,").And.Contain("l'activité de votre entreprise, Atelier Durand, et souhaitons")
            .And.Contain("- votre trésorerie des prochaines semaines ;\n- la répartition de votre chiffre d'affaires entre vos clients.\n")
            .And.EndWith("Votre expert-comptable");
        body.Split("trésorerie").Should().HaveCount(2, "un sujet cité deux fois n'apparaît qu'une fois");
        body.Should().NotContainAny("difficult", "appeler", "surveiller", "€", "%");
    }

    [Fact]
    public void Le_courrier_anglais_et_sans_sujet_reste_un_simple_rendez_vous()
    {
        (string subject, string body) = PreventionLetterTemplates.Write(PreventionLetterTemplates.English, "  ", []);

        subject.Should().Be("Meeting proposal: reviewing the coming months together");
        body.Should().Contain("activity of your company").And.NotContain("In particular").And.EndWith("Your accountant");
    }

    [Theory]
    [InlineData("en-US", "en-US")]
    [InlineData("EN", "en-US")]
    [InlineData("fr-FR", "fr-FR")]
    [InlineData(null, "fr-FR")]
    [InlineData("de-DE", "fr-FR")]
    public void La_langue_du_courrier_est_l_anglais_ou_le_francais(string? demandee, string attendue) =>
        PreventionLetterTemplates.LanguageOf(demandee).Should().Be(attendue);

    [Fact]
    public void Chaque_signal_a_son_sujet_dans_les_deux_langues()
    {
        foreach (PreventionSignalDto signal in Enum.GetValues<PreventionSignalDto>())
        {
            PreventionLetterTemplates.Topic(signal, english: false).Should().NotBe("votre activité");
            PreventionLetterTemplates.Topic(signal, english: true).Should().NotBe("your activity");
        }

        PreventionLetterTemplates.Topic((PreventionSignalDto)99, english: false).Should().Be("votre activité");
    }

    [Fact]
    public void Les_bornes_des_seuils_sont_les_memes_dans_le_contrat_et_le_domaine()
    {
        (PreventionSettingsLimits.DefaultHorizonWeeks, PreventionSettingsLimits.MinHorizonWeeks, PreventionSettingsLimits.MaxHorizonWeeks)
            .Should().Be((PreventionSettings.DefaultHorizonWeeks, PreventionSettings.MinHorizonWeeks, PreventionSettings.MaxHorizonWeeks));
        PreventionSettingsLimits.MaxCashBalance.Should().Be(PreventionSettings.MaxCashBalance);
        (PreventionSettingsLimits.DefaultPaymentDelayRiseDays, PreventionSettingsLimits.MinPaymentDelayRiseDays, PreventionSettingsLimits.MaxPaymentDelayRiseDays)
            .Should().Be((PreventionSettings.DefaultPaymentDelayRiseDays, PreventionSettings.MinPaymentDelayRiseDays, PreventionSettings.MaxPaymentDelayRiseDays));
        (PreventionSettingsLimits.DefaultPeerGapPoints, PreventionSettingsLimits.MinPeerGapPoints, PreventionSettingsLimits.MaxPeerGapPoints)
            .Should().Be((PreventionSettings.DefaultPeerGapPoints, PreventionSettings.MinPeerGapPoints, PreventionSettings.MaxPeerGapPoints));
        (PreventionSettingsLimits.DefaultTopCustomerSharePercent, PreventionSettingsLimits.MinTopCustomerSharePercent, PreventionSettingsLimits.MaxTopCustomerSharePercent)
            .Should().Be((PreventionSettings.DefaultTopCustomerSharePercent, PreventionSettings.MinTopCustomerSharePercent, PreventionSettings.MaxTopCustomerSharePercent));
        (PreventionSettingsLimits.DefaultBilledCollectedGapPercent, PreventionSettingsLimits.MinBilledCollectedGapPercent, PreventionSettingsLimits.MaxBilledCollectedGapPercent)
            .Should().Be((PreventionSettings.DefaultBilledCollectedGapPercent, PreventionSettings.MinBilledCollectedGapPercent, PreventionSettings.MaxBilledCollectedGapPercent));
        (PreventionSettingsLimits.DefaultCallSignalCount, PreventionSettingsLimits.MinCallSignalCount, PreventionSettingsLimits.MaxCallSignalCount)
            .Should().Be((PreventionSettings.DefaultCallSignalCount, PreventionSettings.MinCallSignalCount, PreventionSettings.MaxCallSignalCount));
        Enum.GetValues<PreventionSignal>().Select(s => (int)s).Should().Equal(Enum.GetValues<PreventionSignalDto>().Select(s => (int)s));
    }
}
