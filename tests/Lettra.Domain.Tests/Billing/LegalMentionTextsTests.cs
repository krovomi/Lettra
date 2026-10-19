using Lettra.Calculations.Compliance;

namespace Lettra.Domain.Tests.Billing;

/// <summary>
/// Le texte imprimé d'une mention et sa reconnaissance par le contrôle vont ensemble : reformuler l'une sans l'autre
/// ferait passer une pièce conforme pour non conforme (ou l'inverse) sans que rien d'autre ne le signale.
/// </summary>
public sealed class LegalMentionTextsTests
{
    [Fact]
    public void L_option_sur_les_debits_imprimee_est_reconnue() =>
        LegalMentionTexts.IsVatOnDebits(LegalMentionTexts.VatOnDebits).Should().BeTrue();

    [Fact]
    public void L_indemnite_de_recouvrement_imprimee_est_reconnue()
    {
        LegalMentionTexts.RecoveryIndemnity.Should().Contain("40 €");
        LegalMentionTexts.IsRecoveryIndemnity(LegalMentionTexts.RecoveryIndemnity).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(VatTexts))]
    public void Chaque_texte_legal_de_TVA_cite_le_CGI(string text) =>
        LegalMentionTexts.IsVatLegalText(text).Should().BeTrue();

    public static TheoryData<string> VatTexts()
    {
        TheoryData<string> data = [];
        foreach (SpecialMention mention in MandatoryMentions.Special.Where(m => m.PaperasseId != "autofacturation"))
        {
            data.Add(mention.Text);
        }

        return data;
    }

    [Fact]
    public void Les_mentions_ne_se_confondent_pas()
    {
        LegalMentionTexts.IsVatOnDebits(LegalMentionTexts.RecoveryIndemnity).Should().BeFalse();
        LegalMentionTexts.IsRecoveryIndemnity(LegalMentionTexts.VatOnDebits).Should().BeFalse();
        LegalMentionTexts.IsVatLegalText(LegalMentionTexts.VatOnDebits).Should().BeFalse();
        LegalMentionTexts.IsVatLegalText(LegalMentionTexts.RecoveryIndemnity).Should().BeFalse();
    }

    [Theory]
    [InlineData("option pour la TVA d'après les DÉBITS", true)]
    [InlineData("Pénalités de retard : 10 % par an", false)]
    public void Une_mention_figee_par_une_version_precedente_reste_reconnue(string mention, bool expected) =>
        LegalMentionTexts.IsVatOnDebits(mention).Should().Be(expected);
}
