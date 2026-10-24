using Lettra.Contracts.Exports;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Exports.Domain.Platform;

namespace Lettra.Exports.Tests.Platform;

/// <summary>
/// Le domaine et les contrats portent des énumérations jumelles : mêmes membres, mêmes valeurs. La base enregistre la
/// valeur du domaine et l'API restitue celle du contrat, un écart changerait le sens d'un statut déjà enregistré.
/// </summary>
public sealed class PlatformMappingTests
{
    [Fact]
    public void Les_statuts_du_domaine_et_du_contrat_sont_les_memes() =>
        AssertTwins<PlatformStatus, PlatformStatusDto>();

    [Fact]
    public void Les_voies_du_domaine_et_du_contrat_sont_les_memes() =>
        AssertTwins<PlatformChannel, PlatformChannelDto>();

    [Fact]
    public void Les_syntaxes_du_domaine_et_du_contrat_sont_les_memes() =>
        AssertTwins<EInvoiceFormat, EInvoiceFormatDto>();

    [Theory]
    [InlineData(PlatformStatus.Deposited)]
    [InlineData(PlatformStatus.Collected)]
    [InlineData(PlatformStatus.Rejected)]
    public void Un_statut_fait_l_aller_retour_sans_changer(PlatformStatus status) =>
        status.ToDto().ToDomain().Should().Be(status);

    [Fact]
    public void Une_syntaxe_fait_l_aller_retour_sans_changer() =>
        EInvoiceFormatDto.Ubl.ToDomain().ToDto().Should().Be(EInvoiceFormatDto.Ubl);

    [Fact]
    public void La_reponse_reprend_la_transmission_et_son_historique()
    {
        DateTimeOffset at = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
        PlatformTransmission transmission = new(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "FAC-000042", PlatformChannel.EReporting, null,
            "PA simulée", "PAS-000001", 120m, at,
            [new PlatformStatusEvent(PlatformStatus.Deposited, at), new PlatformStatusEvent(PlatformStatus.Collected, at.AddDays(2), null, 120m)]);

        PlatformTransmissionResponse response = transmission.ToResponse();

        response.Channel.Should().Be(PlatformChannelDto.EReporting);
        response.Format.Should().BeNull();
        response.Status.Should().Be(PlatformStatusDto.Collected);
        response.IsFinal.Should().BeTrue();
        response.CollectedAmount.Should().Be(120m);
        response.History.Select(h => h.Status).Should().Equal(PlatformStatusDto.Deposited, PlatformStatusDto.Collected);
    }

    private static void AssertTwins<TDomain, TContract>()
        where TDomain : struct, Enum
        where TContract : struct, Enum =>
        Enum.GetValues<TDomain>().Select(v => (v.ToString(), Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture)))
            .Should().Equal(Enum.GetValues<TContract>().Select(v => (v.ToString(), Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture))));
}
