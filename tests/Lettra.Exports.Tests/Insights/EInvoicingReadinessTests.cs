using Lettra.Application.Abstractions.Cabinet;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Insights;
using Lettra.Domain.Billing;
using Lettra.Exports.Application.Insights;

namespace Lettra.Exports.Tests.Insights;

/// <summary>
/// Le score de préparation à la facture électronique : sept contrôles notés selon la part des éléments prêts, leur
/// moyenne pour score, et pour la vue cabinet seulement le nombre de contrôles à revoir.
/// </summary>
public sealed class EInvoicingReadinessTests
{
    private static readonly DateOnly Today = new(2026, 10, 26);
    private static readonly CompanyReadinessFacts Complete = new(HasSiren: true, HasSiret: true, HasVatNumber: true, VatFranchise: false, HasIban: true);

    private static PartyReadinessFacts Party(string name, string country = "FR", bool company = true, bool siren = true, bool vat = true) =>
        new(Guid.CreateVersion7(), name, company, country, siren, vat);

    private static PieceReadinessFacts Piece(string number, PlatformStatusDto? status, string country = "FR", string? siren = "123456782", bool business = true) =>
        new(Guid.CreateVersion7(), number, new PartySnapshot("Client", "1 rue", null, "75001", "Paris", country, siren, null, null, null, null, null, business), status);

    [Fact]
    public void Un_dossier_complet_a_100_et_aucun_controle_a_revoir()
    {
        EInvoicingReadinessInput input = new(Complete, [Party("Boulangerie Martin"), Party("Mme Durand", company: false, siren: false)], [Party("Imprimerie")], [Piece("FA-1", PlatformStatusDto.Deposited)]);

        EInvoicingReadinessResponse readiness = EInvoicingReadiness.Compute(input, Today);

        readiness.Score.Should().Be(100);
        readiness.Failing.Should().Be(0);
        readiness.Since.Should().Be(new DateOnly(2026, 7, 28));
        readiness.Checks.Should().OnlyContain(c => c.Ok && c.Gaps.Count == 0);
        readiness.Checks.Single(c => c.Check == EInvoicingCheckDto.EuropeanCustomers).Total.Should().Be(0, "aucun client européen : non concerné");
        EInvoicingReadiness.Indicator(readiness).Should().Be(new DossierIndicator(DossierIndicatorKeys.EInvoicingReadiness, 0, IndicatorLevel.Ok));
    }

    [Fact]
    public void Chaque_controle_compte_ce_qui_manque_et_nomme_les_premiers_elements_a_corriger()
    {
        EInvoicingReadinessInput input = new(
            Complete with { HasVatNumber = false, HasIban = false },
            [Party("Zénith SARL", siren: false), Party("Atelier Dupont", siren: false), Party("Café Lune"), Party("Berlin GmbH", "DE", vat: false), Party("New York Inc", "US", vat: false)],
            [Party("Imprimerie", siren: false), Party("Madrid SL", "ES", siren: false)],
            [Piece("FA-1", PlatformStatusDto.Rejected), Piece("FA-2", null), Piece("FA-3", PlatformStatusDto.Collected), Piece("FA-4", null, "DE", null), Piece("FA-5", null, business: false)]);

        EInvoicingReadinessResponse readiness = EInvoicingReadiness.Compute(input, Today);

        readiness.Checks.Select(c => (c.Check, c.Total, c.Passing, c.Percent)).Should().Equal(
            (EInvoicingCheckDto.CompanyIdentity, 1, 1, 100),
            (EInvoicingCheckDto.CompanyVat, 1, 0, 0),
            (EInvoicingCheckDto.CompanyIban, 1, 0, 0),
            (EInvoicingCheckDto.FrenchCustomers, 3, 1, 33),
            (EInvoicingCheckDto.EuropeanCustomers, 1, 0, 0),
            (EInvoicingCheckDto.Suppliers, 1, 0, 0),
            (EInvoicingCheckDto.Transmissions, 3, 1, 33));
        readiness.Checks[3].Gaps.Select(g => g.Label).Should().Equal("Atelier Dupont", "Zénith SARL");
        readiness.Checks[6].Gaps.Select(g => g.Label).Should().Equal("FA-1", "FA-2");
        readiness.Score.Should().Be(24, "(100 + 0 + 0 + 33 + 0 + 0 + 33) / 7 = 23,7, arrondi");
        readiness.Failing.Should().Be(6);
        EInvoicingReadiness.Indicator(readiness).Should().Be(new DossierIndicator(DossierIndicatorKeys.EInvoicingReadiness, 6, IndicatorLevel.Critical));
    }

    [Theory]
    [InlineData(true, false, 100)]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 100)]
    public void La_franchise_en_base_dispense_du_numero_de_tva(bool vatNumber, bool franchise, int expected)
    {
        EInvoicingReadinessInput input = new(Complete with { HasVatNumber = vatNumber, VatFranchise = franchise }, [], [], []);

        EInvoicingReadiness.Compute(input, Today).Checks.Single(c => c.Check == EInvoicingCheckDto.CompanyVat).Percent.Should().Be(expected);
    }

    [Fact]
    public void Sans_fiche_entreprise_les_trois_controles_de_l_entreprise_echouent_et_les_ecarts_sont_limites()
    {
        PartyReadinessFacts[] many = [.. Enumerable.Range(1, 8).Select(i => Party($"Client {i}", siren: false))];
        EInvoicingReadinessInput input = new(null, many, [], []);

        EInvoicingReadinessResponse readiness = EInvoicingReadiness.Compute(input, Today);

        readiness.Checks.Take(3).Should().OnlyContain(c => !c.Ok);
        readiness.Checks[3].Gaps.Should().HaveCount(EInvoicingReadinessResponse.MaxGaps);
        readiness.Score.Should().Be(43, "(0 + 0 + 0 + 0 + 100 + 100 + 100) / 7");
        EInvoicingReadiness.Indicator(readiness).Level.Should().Be(IndicatorLevel.Critical);
    }

    [Theory]
    [InlineData(100, IndicatorLevel.Ok)]
    [InlineData(86, IndicatorLevel.Info)]
    [InlineData(57, IndicatorLevel.Warning)]
    [InlineData(43, IndicatorLevel.Critical)]
    public void L_urgence_de_la_vue_cabinet_suit_le_score(int score, IndicatorLevel level)
    {
        EInvoicingReadinessResponse readiness = new(Today, Today, score, []);

        EInvoicingReadiness.Indicator(readiness).Level.Should().Be(level);
    }
}
