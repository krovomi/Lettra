using Lettra.Domain.Vigilance;

namespace Lettra.Domain.Tests.Vigilance;

/// <summary>La vérification d'un tiers auprès des registres : ce qui demande un examen, ce qui se refait, et le criblage des noms.</summary>
public sealed class ThirdPartyCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 28);
    private static readonly Guid User = Guid.CreateVersion7();

    private static readonly ThirdPartyFindings Clear = new(2, FreezeScreening.Clear, null, null, null, null, UrssafAnswer.Recognized);

    [Fact]
    public void Sans_alerte_la_verification_vaut_six_mois_puis_est_a_refaire()
    {
        Guid party = Guid.CreateVersion7();
        ThirdPartyCheck check = ThirdPartyCheck.Create(party, ThirdPartyKind.Supplier, Clear, User, Now);

        check.Id.Should().Be(party);
        check.Kind.Should().Be(ThirdPartyKind.Supplier);
        check.CheckedBy.Should().Be(User);
        check.NeedsReview.Should().BeFalse();
        check.StatusOn(Today).Should().Be(ThirdPartyCheckStatus.Clear);
        check.StatusOn(Today.AddDays(ThirdPartyCheck.OutdatedAfterDays)).Should().Be(ThirdPartyCheckStatus.Clear);
        check.StatusOn(Today.AddDays(ThirdPartyCheck.OutdatedAfterDays + 1)).Should().Be(ThirdPartyCheckStatus.Outdated);
    }

    [Theory]
    [InlineData(FreezeScreening.PossibleMatch, null, null)]
    [InlineData(FreezeScreening.Clear, LegalNoticeKind.CollectiveProceeding, null)]
    [InlineData(FreezeScreening.Clear, LegalNoticeKind.StruckOff, null)]
    [InlineData(FreezeScreening.Clear, null, UrssafAnswer.NotRecognized)]
    public void Un_gel_possible_une_annonce_ou_un_code_inconnu_demandent_un_examen_meme_ancien(
        FreezeScreening freeze, LegalNoticeKind? notice, UrssafAnswer? urssaf)
    {
        ThirdPartyCheck check = ThirdPartyCheck.Create(Guid.CreateVersion7(), ThirdPartyKind.Customer,
            new ThirdPartyFindings(null, freeze, "Boris Inventov", notice, Today, "Annonce", urssaf), User, Now);

        check.NeedsReview.Should().BeTrue();
        check.StatusOn(Today.AddYears(2)).Should().Be(ThirdPartyCheckStatus.ToReview);
    }

    [Fact]
    public void Une_nouvelle_verification_remplace_la_precedente_et_ne_garde_que_ce_qui_a_un_sens()
    {
        ThirdPartyCheck check = ThirdPartyCheck.Create(Guid.CreateVersion7(), ThirdPartyKind.Customer,
            new ThirdPartyFindings(1, FreezeScreening.Clear, "ignoré", null, Today, "ignoré", null), User, Now);

        check.FreezeMatch.Should().BeNull("sans correspondance, aucun nom n'est gardé");
        check.NoticeOn.Should().BeNull();
        check.NoticeLabel.Should().BeNull();

        Guid other = Guid.CreateVersion7();
        check.Record(new ThirdPartyFindings(3, FreezeScreening.PossibleMatch, "  " + new string('x', 250) + " ", LegalNoticeKind.StruckOff, Today, "   ", null), other, Now.AddDays(1));

        check.OwnersFound.Should().Be(3);
        check.FreezeMatch.Should().HaveLength(ThirdPartyCheck.MaxNameLength);
        check.Notice.Should().Be(LegalNoticeKind.StruckOff);
        check.NoticeLabel.Should().BeNull("un intitulé vide n'est pas gardé");
        check.CheckedBy.Should().Be(other);
        check.CheckedAt.Should().Be(Now.AddDays(1));
        FluentActions.Invoking(() => check.Record(null!, other, Now)).Should().Throw<ArgumentNullException>();
        ThirdPartyCheckErrors.PartyNotFound.Code.Should().Be(ThirdPartyCheckErrors.PartyNotFoundCode);
    }

    [Theory]
    [InlineData("Viktor Fictivenko", "Viktor Fictivenko", true)]
    [InlineData("FICTIVENKO Viktor", "Viktor Fictivenko", true)]
    [InlineData("Viktor Fiktivenko", "Viktor Fictivenko", true)]
    [InlineData("Société d'Import Imaginaire SARL", "Société d'Import Imaginaire", true)]
    [InlineData("Martin", "Martin Boris", false)]
    [InlineData("Jeanne Martin", "Viktor Fictivenko", false)]
    [InlineData(null, "Viktor Fictivenko", false)]
    public void Le_criblage_tolere_l_ordre_les_accents_et_une_faute_mais_pas_un_nom_seul(string? candidate, string listed, bool expected) =>
        AssetFreezeNames.Matches(candidate, listed).Should().Be(expected);

    [Fact]
    public void Le_premier_nom_ressemblant_est_rendu_tel_qu_inscrit()
    {
        string[] listed = ["Viktor Fictivenko", "V. Fictivenko", "Boris Inventov"];

        AssetFreezeNames.FirstMatch(["Boulangerie Martin", null, " ", "Inventov Boris"], listed).Should().Be("Boris Inventov");
        AssetFreezeNames.FirstMatch(["Boulangerie Martin"], listed).Should().BeNull();
        FluentActions.Invoking(() => AssetFreezeNames.FirstMatch(null!, listed)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => AssetFreezeNames.FirstMatch([], null!)).Should().Throw<ArgumentNullException>();
    }
}
